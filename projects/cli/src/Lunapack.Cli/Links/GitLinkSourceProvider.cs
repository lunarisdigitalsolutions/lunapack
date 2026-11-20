using System.Globalization;
using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Links;

internal sealed class GitLinkSourceProvider(
    IFileSystem fileSystem,
    IGitProcessRunner processRunner,
    GitRefResolver refResolver,
    GitLinkCache cache,
    GitTransportPolicy transportPolicy
) : ILinkSourceProvider
{
    private const int DefaultTimeoutSeconds = 300;

    public bool CanProvide(ProjectConfiguration.Source source) =>
        source is ProjectConfiguration.GitSource;

    public Task<ManifestOperationResult<LinkSourceListing>> ListAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        ProjectConfiguration.Link link,
        ConfiguredSourceIdentity? lockedIdentity,
        CancellationToken cancellationToken
    ) => ListAsync(projectDirectory, source, link, lockedIdentity, null, cancellationToken);

    public async Task<ManifestOperationResult<LinkSourceListing>> ListAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        ProjectConfiguration.Link link,
        ConfiguredSourceIdentity? lockedIdentity,
        ResourceLimits? resourceLimits = null,
        CancellationToken cancellationToken = default
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
        var authorization = await transportPolicy.AuthorizeAsync(
            projectDirectory,
            effectiveSource.Url,
            effectiveSource.Name
        );
        if (!authorization.IsSuccess)
        {
            return ManifestOperationResult<LinkSourceListing>.Failure(
                authorization.Error ?? $"Unable to authorize Git link source '{gitSource.Name}'."
            );
        }
        var transportMode = authorization.Value;

        var resolution = await refResolver.ResolveAsync(
            effectiveSource,
            null,
            cancellationToken,
            transportMode
        );
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

        var limits = resourceLimits ?? ResourceLimits.Default;
        var cached = cache.LoadMetadata(identity, resolvedRef.ResolvedCommit, limits);
        return cached is null
            ? await ListFromGitAsync(
                effectiveSource,
                identity,
                provenance,
                limits,
                transportMode,
                cancellationToken
            )
            : ManifestOperationResult<LinkSourceListing>.Success(
                CreateListing(
                    identity,
                    provenance,
                    string.Empty,
                    cached.Tree,
                    transportMode,
                    effectiveSource.Name,
                    null
                )
            );
    }

    public Task<ManifestOperationResult<IReadOnlyDictionary<string, string>>> MaterializeAsync(
        LinkSourceListing listing,
        IReadOnlyList<string> selectedPaths,
        LinkOperationWorkspace workspace,
        CancellationToken cancellationToken
    ) => MaterializeAsync(listing, selectedPaths, workspace, null, cancellationToken);

    public async Task<
        ManifestOperationResult<IReadOnlyDictionary<string, string>>
    > MaterializeAsync(
        LinkSourceListing listing,
        IReadOnlyList<string> selectedPaths,
        LinkOperationWorkspace workspace,
        ResourceLimits? resourceLimits = null,
        CancellationToken cancellationToken = default
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
            var cachedContents = contents[selectedPath];
            await using var source = new MemoryStream(cachedContents, writable: false);
            var snapshot = await workspace.WriteAsync(
                selectedPath,
                source,
                cachedContents.Length,
                cancellationToken
            );
            if (snapshot.Value is not { } snapshotPath)
            {
                return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                    snapshot.Error ?? $"Unable to snapshot link source file '{selectedPath}'.",
                    snapshot.ErrorKind
                );
            }

            snapshots.Add(selectedPath, snapshotPath);
        }

        return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Success(snapshots);
    }

    private async Task<ManifestOperationResult<LinkSourceListing>> ListFromGitAsync(
        ProjectConfiguration.GitSource source,
        ConfiguredSourceIdentity identity,
        GitSourceProvenance provenance,
        ResourceLimits resourceLimits,
        GitTransportMode transportMode,
        CancellationToken cancellationToken
    )
    {
        var repository = CreateRepositoryDirectory();
        var fetched = await FetchAsync(
            repository,
            source.Url,
            source.Name,
            provenance.ResolvedCommit,
            CreateTimeout(source),
            transportMode,
            cancellationToken
        );
        if (!fetched.IsSuccess)
        {
            GitTemporaryWorkspace.Delete(fileSystem, repository);
            return ManifestOperationResult<LinkSourceListing>.Failure(
                fetched.Error ?? "Unable to fetch the Git link source."
            );
        }

        var listed = await processRunner.RunBoundedRecordsAsync(
            ["-C", repository, "ls-tree", "-r", "-z", "FETCH_HEAD"],
            CreateTimeout(source),
            resourceLimits.FilesPerGraph,
            "Git link source tree enumeration",
            0,
            64L * 1024 * 1024,
            1L * 1024 * 1024,
            transportMode,
            source.Url,
            cancellationToken
        );
        if (listed.Value is not { } output)
        {
            GitTemporaryWorkspace.Delete(fileSystem, repository);
            return ManifestOperationResult<LinkSourceListing>.Failure(
                GitSourceFailureFormatter.Format(
                    source.Name,
                    listed.Error,
                    "Unable to enumerate the Git link source tree."
                )
            );
        }
        if (output.StandardOutputTruncated)
        {
            GitTemporaryWorkspace.Delete(fileSystem, repository);
            return ManifestOperationResult<LinkSourceListing>.Failure(
                "Git link tree output was truncated; the source cannot be resolved safely."
            );
        }

        var tree = ParseTree(output.StandardOutput, provenance.Path);
        var persisted = cache.SaveMetadata(
            new GitLinkCacheMetadata
            {
                ResolvedCommit = provenance.ResolvedCommit,
                Source = identity,
                Tree = [.. tree],
            },
            resourceLimits
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
                transportMode,
                source.Name,
                () => GitTemporaryWorkspace.Delete(fileSystem, repository)
            )
        );
    }

    private async Task<ManifestOperationResult<bool>> FetchAsync(
        string repository,
        string sourceUrl,
        string sourceName,
        string resolvedCommit,
        TimeSpan timeout,
        GitTransportMode transportMode,
        CancellationToken cancellationToken
    )
    {
        var commands = ((
            string[] Arguments,
            GitTransportMode TransportMode,
            string? RepositoryUrl
        )[])
            [
                (["init", "--quiet", repository], GitTransportMode.SecureOnly, (string?)null),
                (
                    ["-C", repository, "remote", "add", "--", "origin", sourceUrl],
                    GitTransportMode.SecureOnly,
                    null
                ),
                (
                    [
                        "-C",
                        repository,
                        "fetch",
                        "--depth=1",
                        "--filter=blob:none",
                        "origin",
                        resolvedCommit,
                    ],
                    transportMode,
                    sourceUrl
                ),
            ];
        foreach (var (arguments, commandTransportMode, repositoryUrl) in commands)
        {
            var result = await processRunner.RunAsync(
                arguments,
                timeout,
                commandTransportMode,
                repositoryUrl,
                cancellationToken
            );
            if (!result.IsSuccess)
            {
                return ManifestOperationResult<bool>.Failure(
                    GitSourceFailureFormatter.Format(
                        sourceName,
                        result.Error,
                        "Git link source fetch failed."
                    )
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
        var repository = listing.RootDirectory;
        var createdRepository = repository.Length == 0;
        var materializationListing = listing;
        if (createdRepository)
        {
            repository = CreateRepositoryDirectory();
            materializationListing = listing with { RootDirectory = repository };
        }

        try
        {
            if (createdRepository)
            {
                var fetched = await FetchAsync(
                    repository,
                    gitSource.Url,
                    listing.SourceName,
                    gitSource.ResolvedCommit,
                    TimeSpan.FromSeconds(DefaultTimeoutSeconds),
                    listing.TransportMode,
                    cancellationToken
                );
                if (!fetched.IsSuccess)
                {
                    return ManifestOperationResult<bool>.Failure(
                        fetched.Error ?? "Unable to fetch the Git link source."
                    );
                }
            }

            var checkout = await CheckoutPathsAsync(
                repository,
                gitSource.Path,
                missingPaths,
                listing.TransportMode,
                gitSource.Url,
                listing.SourceName,
                cancellationToken
            );
            if (!checkout.IsSuccess)
            {
                return checkout;
            }

            return ReadCheckedOutFiles(materializationListing, gitSource, missingPaths, contents);
        }
        finally
        {
            if (createdRepository)
            {
                GitTemporaryWorkspace.Delete(fileSystem, repository);
            }
        }
    }

    private async Task<ManifestOperationResult<bool>> CheckoutPathsAsync(
        string repository,
        string? basePath,
        IReadOnlyList<string> selectedPaths,
        GitTransportMode transportMode,
        string sourceUrl,
        string sourceName,
        CancellationToken cancellationToken
    )
    {
        var repositoryPaths = selectedPaths.Select(path =>
            string.IsNullOrEmpty(basePath) ? path : $"{basePath}/{path}"
        );
        var commands = new[]
        {
            (
                new[] { "-C", repository, "sparse-checkout", "init", "--no-cone" },
                GitTransportMode.SecureOnly,
                (string?)null
            ),
            (
                (string[])
                    [
                        "-C",
                        repository,
                        "sparse-checkout",
                        "set",
                        "--no-cone",
                        "--",
                        .. repositoryPaths.Select(path => $"/{path}"),
                    ],
                GitTransportMode.SecureOnly,
                null
            ),
            (
                new[]
                {
                    "-c",
                    "core.autocrlf=false",
                    "-c",
                    "core.eol=lf",
                    "-c",
                    "core.symlinks=false",
                    "-C",
                    repository,
                    "checkout",
                    "--quiet",
                    "--detach",
                    "FETCH_HEAD",
                },
                transportMode,
                sourceUrl
            ),
        };
        foreach (var (arguments, commandTransportMode, repositoryUrl) in commands)
        {
            var result = await processRunner.RunAsync(
                arguments,
                TimeSpan.FromSeconds(DefaultTimeoutSeconds),
                commandTransportMode,
                repositoryUrl,
                cancellationToken
            );
            if (!result.IsSuccess)
            {
                return ManifestOperationResult<bool>.Failure(
                    GitSourceFailureFormatter.Format(
                        sourceName,
                        result.Error,
                        "Git link source checkout failed."
                    )
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
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
        GitTransportMode transportMode,
        string sourceName,
        Action? cleanup
    ) =>
        new(identity, provenance, repository, [.. tree.Select(entry => entry.Path)])
        {
            BlobIds = tree.ToDictionary(
                entry => entry.Path,
                entry => entry.BlobId,
                StringComparer.Ordinal
            ),
            TransportMode = transportMode,
            SourceName = sourceName,
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
