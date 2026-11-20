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
    GitLinkCache cache
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

        var limits = resourceLimits ?? ResourceLimits.Default;
        var cached = cache.LoadMetadata(identity, resolvedRef.ResolvedCommit, limits);
        if (cached is null)
        {
            return await ListFromGitAsync(
                effectiveSource,
                identity,
                provenance,
                limits,
                cancellationToken
            );
        }

        if (cached.Tree.Count > limits.FilesPerGraph)
        {
            return ManifestOperationResult<LinkSourceListing>.Failure(
                $"Git link source cache exceeds its {limits.FilesPerGraph} file limit (at least {cached.Tree.Count} files observed).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        return ManifestOperationResult<LinkSourceListing>.Success(
            CreateListing(identity, provenance, string.Empty, cached.Tree, null)
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

        var limits = resourceLimits ?? ResourceLimits.Default;
        var budget = new LinkSnapshotBudget(limits);
        var cached = await SnapshotCachedBlobsAsync(
            listing,
            gitSource,
            selectedPaths,
            workspace,
            budget,
            cancellationToken
        );
        if (cached.Value is not { } cachedSnapshots)
        {
            return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                cached.Error ?? "Unable to read cached Git link content.",
                cached.ErrorKind
            );
        }

        var snapshots = cachedSnapshots.Snapshots;
        var missingPaths = cachedSnapshots.MissingPaths;
        if (missingPaths.Count > 0)
        {
            var checkout = await CheckoutAsync(
                listing,
                gitSource,
                missingPaths,
                workspace,
                budget,
                snapshots,
                cancellationToken
            );
            if (!checkout.IsSuccess)
            {
                return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                    checkout.Error ?? "Unable to materialize Git link content.",
                    checkout.ErrorKind
                );
            }
        }

        return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Success(snapshots);
    }

    private async Task<ManifestOperationResult<CachedLinkSnapshots>> SnapshotCachedBlobsAsync(
        LinkSourceListing listing,
        GitSourceProvenance gitSource,
        IReadOnlyList<string> selectedPaths,
        LinkOperationWorkspace workspace,
        LinkSnapshotBudget budget,
        CancellationToken cancellationToken
    )
    {
        var snapshots = new Dictionary<string, string>(StringComparer.Ordinal);
        var missingPaths = new List<string>();
        foreach (var selectedPath in selectedPaths)
        {
            var cachedBlobPath = cache.TryGetBlobPath(
                listing.Identity,
                gitSource.ResolvedCommit,
                listing.BlobIds[selectedPath],
                Math.Max(0, budget.RemainingBytes)
            );
            if (cachedBlobPath is null)
            {
                missingPaths.Add(selectedPath);
                continue;
            }

            var observed = budget.AddFilePath(
                selectedPath,
                fileSystem.FileInfo.New(cachedBlobPath).Length
            );
            if (!observed.IsSuccess)
            {
                return ManifestOperationResult<CachedLinkSnapshots>.Failure(
                    observed.Error!,
                    observed.ErrorKind
                );
            }

            var snapshot = await SnapshotFileAsync(
                selectedPath,
                cachedBlobPath,
                workspace,
                null,
                cancellationToken
            );
            if (snapshot.Value is not { } snapshotPath)
            {
                return ManifestOperationResult<CachedLinkSnapshots>.Failure(
                    snapshot.Error ?? $"Unable to snapshot link source file '{selectedPath}'.",
                    snapshot.ErrorKind
                );
            }

            snapshots.Add(selectedPath, snapshotPath);
        }

        return ManifestOperationResult<CachedLinkSnapshots>.Success(
            new CachedLinkSnapshots(missingPaths, snapshots)
        );
    }

    private async Task<ManifestOperationResult<LinkSourceListing>> ListFromGitAsync(
        ProjectConfiguration.GitSource source,
        ConfiguredSourceIdentity identity,
        GitSourceProvenance provenance,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    )
    {
        var repository = CreateRepositoryDirectory();
        var fetched = await FetchAsync(
            repository,
            source.Url,
            provenance.ResolvedCommit,
            CreateTimeout(source),
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
            ["-C", repository, "ls-tree", "-r", "-l", "-z", "FETCH_HEAD"],
            CreateTimeout(source),
            resourceLimits.FilesPerGraph,
            "Git link source tree enumeration",
            0,
            64L * 1024 * 1024,
            1L * 1024 * 1024,
            cancellationToken
        );
        if (listed.Value is not { } output)
        {
            GitTemporaryWorkspace.Delete(fileSystem, repository);
            return ManifestOperationResult<LinkSourceListing>.Failure(
                listed.Error ?? "Unable to enumerate the Git link source tree.",
                listed.ErrorKind
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
                () => GitTemporaryWorkspace.Delete(fileSystem, repository)
            )
        );
    }

    private async Task<ManifestOperationResult<bool>> FetchAsync(
        string repository,
        string sourceUrl,
        string resolvedCommit,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var commands = new[]
        {
            new[] { "init", "--quiet", repository },
            ["-C", repository, "remote", "add", "origin", sourceUrl],
            [
                "-C",
                repository,
                "fetch",
                "--depth=1",
                "--filter=blob:none",
                "origin",
                resolvedCommit,
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
        LinkOperationWorkspace workspace,
        LinkSnapshotBudget budget,
        Dictionary<string, string> snapshots,
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
                    gitSource.ResolvedCommit,
                    TimeSpan.FromSeconds(DefaultTimeoutSeconds),
                    cancellationToken
                );
                if (!fetched.IsSuccess)
                {
                    return ManifestOperationResult<bool>.Failure(
                        fetched.Error ?? "Unable to fetch the Git link source."
                    );
                }
            }

            var preflight = await PreflightMissingBlobsAsync(
                materializationListing,
                missingPaths,
                repository,
                budget,
                cancellationToken
            );
            if (!preflight.IsSuccess)
            {
                return preflight;
            }

            var checkout = await CheckoutPathsAsync(
                repository,
                gitSource.Path,
                missingPaths,
                cancellationToken
            );
            if (!checkout.IsSuccess)
            {
                return checkout;
            }

            return await ReadCheckedOutFilesAsync(
                materializationListing,
                gitSource,
                missingPaths,
                workspace,
                snapshots,
                cancellationToken
            );
        }
        finally
        {
            if (createdRepository)
            {
                GitTemporaryWorkspace.Delete(fileSystem, repository);
            }
        }
    }

    private async Task<ManifestOperationResult<bool>> PreflightMissingBlobsAsync(
        LinkSourceListing listing,
        IReadOnlyList<string> missingPaths,
        string repository,
        LinkSnapshotBudget budget,
        CancellationToken cancellationToken
    )
    {
        var repositoryPaths = missingPaths.Select(path =>
            string.IsNullOrEmpty(listing.GitSource?.Path)
                ? path
                : $"{listing.GitSource.Path}/{path}"
        );
        var listed = await processRunner.RunBoundedRecordsAsync(
            ["-C", repository, "ls-tree", "-r", "-l", "-z", "FETCH_HEAD", "--", .. repositoryPaths],
            TimeSpan.FromSeconds(DefaultTimeoutSeconds),
            missingPaths.Count,
            "Git link source blob preflight",
            0,
            64L * 1024 * 1024,
            1L * 1024 * 1024,
            cancellationToken
        );
        if (listed.Value is not { } output)
        {
            return ManifestOperationResult<bool>.Failure(
                listed.Error ?? "Unable to preflight Git link source blobs.",
                listed.ErrorKind
            );
        }

        var entries = ParseTree(output.StandardOutput, listing.GitSource?.Path)
            .ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        foreach (var missingPath in missingPaths)
        {
            if (
                !entries.TryGetValue(missingPath, out var entry)
                || entry.Size is not { } size
                || !string.Equals(
                    entry.BlobId,
                    listing.BlobIds[missingPath],
                    StringComparison.Ordinal
                )
            )
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Git link source blob metadata for '{missingPath}' changed or is unavailable."
                );
            }

            var observed = budget.AddFilePath(missingPath, size);
            if (!observed.IsSuccess)
            {
                return observed;
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private async Task<ManifestOperationResult<bool>> CheckoutPathsAsync(
        string repository,
        string? basePath,
        IReadOnlyList<string> selectedPaths,
        CancellationToken cancellationToken
    )
    {
        var repositoryPaths = selectedPaths.Select(path =>
            string.IsNullOrEmpty(basePath) ? path : $"{basePath}/{path}"
        );
        var commands = new[]
        {
            new[] { "-C", repository, "sparse-checkout", "init", "--no-cone" },
            [
                "-C",
                repository,
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
                repository,
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

        return ManifestOperationResult<bool>.Success(true);
    }

    private async Task<ManifestOperationResult<bool>> ReadCheckedOutFilesAsync(
        LinkSourceListing listing,
        GitSourceProvenance gitSource,
        IReadOnlyList<string> missingPaths,
        LinkOperationWorkspace workspace,
        Dictionary<string, string> snapshots,
        CancellationToken cancellationToken
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
            await using (var verification = fileSystem.File.OpenRead(filePath))
            {
                if (!GitObjectId.Matches(blobId, verification, verification.Length))
                {
                    return ManifestOperationResult<bool>.Failure(
                        $"Git link source file '{missingPath}' does not match its recorded object id."
                    );
                }
            }

            var snapshot = await SnapshotFileAsync(
                missingPath,
                filePath,
                workspace,
                null,
                cancellationToken
            );
            if (snapshot.Value is not { } snapshotPath)
            {
                return ManifestOperationResult<bool>.Failure(
                    snapshot.Error ?? $"Unable to snapshot Git link source file '{missingPath}'.",
                    snapshot.ErrorKind
                );
            }

            snapshots.Add(missingPath, snapshotPath);
            cache.SaveBlobFromPath(listing.Identity, gitSource.ResolvedCommit, blobId, filePath);
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private async Task<ManifestOperationResult<string>> SnapshotFileAsync(
        string sourcePath,
        string filePath,
        LinkOperationWorkspace workspace,
        LinkSnapshotBudget? budget,
        CancellationToken cancellationToken
    )
    {
        var length = fileSystem.FileInfo.New(filePath).Length;
        if (budget is not null)
        {
            var observed = budget.AddFile(length);
            if (!observed.IsSuccess)
            {
                return ManifestOperationResult<string>.Failure(observed.Error!, observed.ErrorKind);
            }
        }

        await using var source = fileSystem.File.OpenRead(filePath);
        return await workspace.WriteAsync(sourcePath, source, length, cancellationToken);
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
            BlobSizes = tree.Where(entry => entry.Size is not null)
                .ToDictionary(
                    entry => entry.Path,
                    entry => entry.Size!.Value,
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
                fields.Length is < 3 or > 4
                || !string.Equals(fields[1], "blob", StringComparison.Ordinal)
                || !IsRegularFileMode(fields[0]);
            long? size = null;
            if (!treeEntryIsInvalid && fields.Length == 4)
            {
                if (
                    !long.TryParse(
                        fields[3],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedSize
                    )
                    || parsedSize < 0
                )
                {
                    treeEntryIsInvalid = true;
                }
                else
                {
                    size = parsedSize;
                }
            }

            if (treeEntryIsInvalid)
            {
                continue;
            }

            var path = ProjectPath.Normalize(line[(separator + 1)..]);
            if (prefix.Length > 0 && !path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            entries.Add(
                new GitLinkCacheEntry
                {
                    BlobId = fields[2],
                    Path = path[prefix.Length..],
                    Size = size,
                }
            );
        }

        return entries;
    }

    private static bool IsRegularFileMode(string mode) =>
        string.Equals(mode, "100644", StringComparison.Ordinal)
        || string.Equals(mode, "100755", StringComparison.Ordinal);

    private sealed record CachedLinkSnapshots(
        List<string> MissingPaths,
        Dictionary<string, string> Snapshots
    );

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
