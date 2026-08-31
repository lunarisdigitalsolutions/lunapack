using System.Globalization;
using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Links;

internal sealed class GitLinkSourceProvider(
    IFileSystem fileSystem,
    IGitProcessRunner processRunner,
    GitRefResolver refResolver,
    GitLinkCache cache
) : ILinkSourceProvider
{
    private const int DefaultTimeoutSeconds = 300;

    public bool CanProvide(ProjectConfiguration.Source source) =>
        source is ProjectConfiguration.GitSource;

    public async Task<ManifestOperationResult<LinkSourceListing>> ListAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        ProjectConfiguration.Link link,
        ConfiguredSourceIdentity? lockedIdentity,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(link);

        if (source is not ProjectConfiguration.GitSource gitSource)
        {
            return ManifestOperationResult<LinkSourceListing>.Failure(
                "Git link resolution requires a Git source."
            );
        }

        var effectiveSource = link.Ref is { } linkRef
            ? gitSource with
            {
                Ref = linkRef,
            }
            : gitSource;
        var resolution = await refResolver.ResolveAsync(effectiveSource, null, cancellationToken);
        if (resolution.Value is not { } resolvedRef)
        {
            return ManifestOperationResult<LinkSourceListing>.Failure(
                resolution.Error
                    ?? $"Unable to resolve the Git ref for link source '{gitSource.Name}'."
            );
        }

        var identity = ConfiguredSourceIdentity.Create(gitSource);
        var provenance = new GitSourceProvenance
        {
            Path = ProjectPath.NormalizeOptional(gitSource.Path)?.Trim('/'),
            Ref = effectiveSource.Ref,
            ResolvedCommit = resolvedRef.ResolvedCommit,
            Url = gitSource.Url,
        };

        var cached = cache.LoadMetadata(identity, resolvedRef.ResolvedCommit);
        return cached is null
            ? await ListFromGitAsync(effectiveSource, identity, provenance, cancellationToken)
            : ManifestOperationResult<LinkSourceListing>.Success(
                CreateListing(identity, provenance, string.Empty, cached.Tree, null)
            );
    }

    public async Task<
        ManifestOperationResult<IReadOnlyDictionary<string, string>>
    > MaterializeAsync(
        LinkSourceListing listing,
        IReadOnlyList<string> selectedPaths,
        LinkOperationWorkspace workspace,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(selectedPaths);
        ArgumentNullException.ThrowIfNull(workspace);

        if (listing.GitSource is not { } gitSource)
        {
            return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                "Git link materialization requires Git source provenance."
            );
        }

        var commit = gitSource.ResolvedCommit;
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var missingPaths = new List<string>();
        foreach (var selectedPath in selectedPaths)
        {
            var cachedBlob = cache.TryReadBlob(
                listing.Identity,
                commit,
                listing.BlobIds[selectedPath]
            );
            if (cachedBlob is null)
            {
                missingPaths.Add(selectedPath);
            }
            else
            {
                contents.Add(selectedPath, cachedBlob);
            }
        }

        if (missingPaths.Count > 0)
        {
            var checkout = await CheckoutAsync(
                listing,
                gitSource,
                missingPaths,
                contents,
                cancellationToken
            );
            if (!checkout.IsSuccess)
            {
                return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                    checkout.Error ?? "Unable to materialize Git link content."
                );
            }
        }

        var snapshots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var selectedPath in selectedPaths)
        {
            snapshots.Add(selectedPath, workspace.Write(selectedPath, contents[selectedPath]));
        }

        return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Success(snapshots);
    }

    private async Task<ManifestOperationResult<LinkSourceListing>> ListFromGitAsync(
        ProjectConfiguration.GitSource source,
        ConfiguredSourceIdentity identity,
        GitSourceProvenance provenance,
        CancellationToken cancellationToken
    )
    {
        var repository = CreateRepositoryDirectory();
        var fetched = await FetchAsync(repository, source, provenance, cancellationToken);
        if (!fetched.IsSuccess)
        {
            GitTemporaryWorkspace.Delete(fileSystem, repository);
            return ManifestOperationResult<LinkSourceListing>.Failure(
                fetched.Error ?? "Unable to fetch the Git link source."
            );
        }

        var listed = await processRunner.RunAsync(
            ["-C", repository, "ls-tree", "-r", "-z", "FETCH_HEAD"],
            CreateTimeout(source),
            cancellationToken
        );
        if (listed.Value is not { } output)
        {
            GitTemporaryWorkspace.Delete(fileSystem, repository);
            return ManifestOperationResult<LinkSourceListing>.Failure(
                listed.Error ?? "Unable to enumerate the Git link source tree."
            );
        }

        var tree = ParseTree(output.StandardOutput, provenance.Path);
        var persisted = cache.SaveMetadata(
            new GitLinkCacheMetadata
            {
                ResolvedCommit = provenance.ResolvedCommit,
                Source = identity,
                Tree = [.. tree],
            }
        );
        if (!persisted.IsSuccess)
        {
            GitTemporaryWorkspace.Delete(fileSystem, repository);
            return ManifestOperationResult<LinkSourceListing>.Failure(
                persisted.Error ?? "Unable to persist Git link cache metadata."
            );
        }

        return ManifestOperationResult<LinkSourceListing>.Success(
            CreateListing(
                identity,
                provenance,
                repository,
                tree,
                () => GitTemporaryWorkspace.Delete(fileSystem, repository)
            )
        );
    }

    private async Task<ManifestOperationResult<bool>> FetchAsync(
        string repository,
        ProjectConfiguration.GitSource source,
        GitSourceProvenance provenance,
        CancellationToken cancellationToken
    )
    {
        var timeout = CreateTimeout(source);
        var commands = new[]
        {
            new[] { "init", "--quiet", repository },
            ["-C", repository, "remote", "add", "origin", source.Url],
            [
                "-C",
                repository,
                "fetch",
                "--depth=1",
                "--filter=blob:none",
                "origin",
                provenance.ResolvedCommit,
            ],
        };
        foreach (var arguments in commands)
        {
            var result = await processRunner.RunAsync(arguments, timeout, cancellationToken);
            if (!result.IsSuccess)
            {
                return ManifestOperationResult<bool>.Failure(
                    result.Error ?? "Git link source fetch failed."
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private async Task<ManifestOperationResult<bool>> CheckoutAsync(
        LinkSourceListing listing,
        GitSourceProvenance gitSource,
        IReadOnlyList<string> missingPaths,
        Dictionary<string, byte[]> contents,
        CancellationToken cancellationToken
    )
    {
        if (listing.RootDirectory.Length == 0)
        {
            return ManifestOperationResult<bool>.Failure(
                "Cached Git link content is incomplete and the source repository is unavailable."
            );
        }

        var basePath = gitSource.Path;
        var repositoryPaths = missingPaths.Select(path =>
            string.IsNullOrEmpty(basePath) ? path : $"{basePath}/{path}"
        );
        var commands = new[]
        {
            new[] { "-C", listing.RootDirectory, "sparse-checkout", "init", "--no-cone" },
            [
                "-C",
                listing.RootDirectory,
                "sparse-checkout",
                "set",
                "--no-cone",
                "--",
                .. repositoryPaths.Select(path => $"/{path}"),
            ],
            [
                "-c",
                "core.autocrlf=false",
                "-c",
                "core.eol=lf",
                "-c",
                "core.symlinks=false",
                "-C",
                listing.RootDirectory,
                "checkout",
                "--quiet",
                "--detach",
                "FETCH_HEAD",
            ],
        };
        foreach (var arguments in commands)
        {
            var result = await processRunner.RunAsync(
                arguments,
                TimeSpan.FromSeconds(DefaultTimeoutSeconds),
                cancellationToken
            );
            if (!result.IsSuccess)
            {
                return ManifestOperationResult<bool>.Failure(
                    result.Error ?? "Git link source checkout failed."
                );
            }
        }

        return ReadCheckedOutFiles(listing, gitSource, missingPaths, contents);
    }

    private ManifestOperationResult<bool> ReadCheckedOutFiles(
        LinkSourceListing listing,
        GitSourceProvenance gitSource,
        IReadOnlyList<string> missingPaths,
        Dictionary<string, byte[]> contents
    )
    {
        var basePath = gitSource.Path;
        foreach (var missingPath in missingPaths)
        {
            var repositoryPath = string.IsNullOrEmpty(basePath)
                ? missingPath
                : $"{basePath}/{missingPath}";
            var filePath = fileSystem.Path.Combine([
                listing.RootDirectory,
                .. repositoryPath.Split('/'),
            ]);
            if (!fileSystem.File.Exists(filePath))
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Git link source file '{missingPath}' was not materialized."
                );
            }

            var blobId = listing.BlobIds[missingPath];
            var fileContents = fileSystem.File.ReadAllBytes(filePath);
            if (!GitObjectId.Matches(blobId, fileContents))
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Git link source file '{missingPath}' does not match its recorded object id."
                );
            }

            contents.Add(missingPath, fileContents);
            cache.SaveBlob(listing.Identity, gitSource.ResolvedCommit, blobId, fileContents);
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private static LinkSourceListing CreateListing(
        ConfiguredSourceIdentity identity,
        GitSourceProvenance provenance,
        string repository,
        IReadOnlyList<GitLinkCacheEntry> tree,
        Action? cleanup
    ) =>
        new(identity, provenance, repository, [.. tree.Select(entry => entry.Path)])
        {
            BlobIds = tree.ToDictionary(
                entry => entry.Path,
                entry => entry.BlobId,
                StringComparer.Ordinal
            ),
            Cleanup = cleanup,
        };

    internal static IReadOnlyList<GitLinkCacheEntry> ParseTree(string output, string? basePath)
    {
        var prefix = string.IsNullOrEmpty(basePath) ? string.Empty : $"{basePath}/";
        var entries = new List<GitLinkCacheEntry>();
        foreach (var line in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('\t', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var fields = line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var treeEntryIsInvalid =
                fields.Length != 3
                || !string.Equals(fields[1], "blob", StringComparison.Ordinal)
                || !IsRegularFileMode(fields[0]);
            if (treeEntryIsInvalid)
            {
                continue;
            }

            var path = ProjectPath.Normalize(line[(separator + 1)..]);
            if (prefix.Length > 0 && !path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            entries.Add(new GitLinkCacheEntry { BlobId = fields[2], Path = path[prefix.Length..] });
        }

        return entries;
    }

    private static bool IsRegularFileMode(string mode) =>
        string.Equals(mode, "100644", StringComparison.Ordinal)
        || string.Equals(mode, "100755", StringComparison.Ordinal);

    private static TimeSpan CreateTimeout(ProjectConfiguration.GitSource source) =>
        TimeSpan.FromSeconds(source.TimeoutSeconds ?? DefaultTimeoutSeconds);

    private string CreateRepositoryDirectory() =>
        fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            "lunapack",
            "links",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
        );
}
