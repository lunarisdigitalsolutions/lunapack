using System.Globalization;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using NuGet.Versioning;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli.Sources.Git;

internal sealed class GitPackDiscovery(
    IFileSystem fileSystem,
    IGitProcessRunner processRunner,
    GitRefResolver refResolver,
    GitSourceCache cache,
    CliConsole console
)
{
    private const int DefaultTimeoutSeconds = 300;
    private const long MaximumEnumerationOutputBytes = 64L * 1024 * 1024;
    private const long MaximumEnumerationRecordBytes = 1L * 1024 * 1024;

    private static readonly IDeserializer _deserializer = new StaticDeserializerBuilder(
        new LunapackYamlContext()
    )
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new PackParameterYamlTypeConverter())
        .WithTypeConverter(new ScalarValueDictionaryYamlTypeConverter())
        .Build();

    private readonly CliConsole _console = console;

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectConfiguration.GitSource source,
        int sourceOrder,
        ResourceLimits? resourceLimits = null,
        CancellationToken cancellationToken = default
    )
    {
        var effectiveResourceLimits = resourceLimits ?? ResourceLimits.Default;
        if (!IsSafeRepositoryPath(source.Path))
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                "Git source paths must be repository-relative and must not contain '..'."
            );
        }

        var resolution = await refResolver.ResolveAsync(
            source,
            cachedDefaultBranch: null,
            cancellationToken
        );
        if (resolution.Value is not { } resolvedSource)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                resolution.Error ?? "Unable to resolve Git source."
            );
        }

        var discovered = await DiscoverAsync(
            source,
            resolvedSource.ResolvedCommit,
            effectiveResourceLimits,
            cancellationToken
        );
        if (discovered.Value is not { } packs)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                discovered.Error ?? "Unable to discover Git packs.",
                discovered.ErrorKind
            );
        }

        var newEntry = new GitSourceCacheEntry
        {
            Source = GitSourceCacheIdentity.Create(source),
            ResolvedCommit = resolvedSource.ResolvedCommit,
            DefaultBranch = resolvedSource.DefaultBranch,
            Packs = packs,
        };
        var currentEntry = cache.Load(projectDirectory, source, effectiveResourceLimits);
        if (currentEntry.Value is { } entry && GitSourceCache.AreEquivalent(entry, newEntry))
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(
                CreateCatalog(source, sourceOrder, resolvedSource.ResolvedCommit, packs)
            );
        }

        var saved = cache.Save(projectDirectory, newEntry, effectiveResourceLimits);
        return saved.IsSuccess
            ? ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(
                CreateCatalog(source, sourceOrder, resolvedSource.ResolvedCommit, packs)
            )
            : ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                saved.Error ?? "Unable to persist Git source cache.",
                saved.ErrorKind
            );
    }

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseCommitAsync(
        ProjectConfiguration.GitSource source,
        int sourceOrder,
        string resolvedCommit,
        ResourceLimits? resourceLimits = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsSafeRepositoryPath(source.Path))
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                "Git source paths must be repository-relative and must not contain '..'."
            );
        }

        if (!GitRefResolver.IsCommit(resolvedCommit))
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                $"Git commit '{resolvedCommit}' is not a full commit identity."
            );
        }

        var discovered = await DiscoverAsync(
            source,
            resolvedCommit,
            resourceLimits ?? ResourceLimits.Default,
            cancellationToken
        );
        return discovered.Value is { } packs
            ? ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(
                CreateCatalog(source, sourceOrder, resolvedCommit, packs)
            )
            : ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                discovered.Error ?? "Unable to discover locked Git packs.",
                discovered.ErrorKind
            );
    }

    public ManifestOperationResult<IReadOnlyList<CatalogPack>> BrowseCached(
        string projectDirectory,
        ProjectConfiguration.GitSource source,
        int sourceOrder,
        ResourceLimits? resourceLimits = null
    )
    {
        var loadedCache = cache.Load(projectDirectory, source, resourceLimits);
        if (!loadedCache.IsSuccess)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                loadedCache.Error ?? "Unable to read Git source cache.",
                loadedCache.ErrorKind
            );
        }

        return loadedCache.Value is { } entry
            ? ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(
                CreateCatalog(source, sourceOrder, entry.ResolvedCommit, entry.Packs)
            )
            : ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success([]);
    }

    private async Task<ManifestOperationResult<List<GitCachedPack>>> DiscoverAsync(
        ProjectConfiguration.GitSource source,
        string resolvedCommit,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    )
    {
        var workspace = fileSystem.Path.Combine(
            Path.GetTempPath(),
            "lunapack",
            "git-sources",
            "temporary",
            Guid.NewGuid().ToString("N")
        );
        var timeout = TimeSpan.FromSeconds(source.TimeoutSeconds ?? DefaultTimeoutSeconds);
        try
        {
            var preparationFailure = await PrepareWorkspaceAsync(
                source,
                resolvedCommit,
                workspace,
                timeout,
                cancellationToken
            );
            if (preparationFailure is not null)
            {
                return preparationFailure;
            }

            var listed = await ListManifestPathsAsync(
                source,
                resolvedCommit,
                workspace,
                timeout,
                resourceLimits,
                cancellationToken
            );
            if (listed.Value is not { } paths)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    listed.Error ?? "Unable to list Git repository paths.",
                    listed.ErrorKind
                );
            }

            var packs = await DiscoverPacksAsync(
                source,
                resolvedCommit,
                workspace,
                timeout,
                ParseRecords(paths),
                resourceLimits,
                cancellationToken
            );
            return packs.Value is { } discoveredPacks
                ? ManifestOperationResult<List<GitCachedPack>>.Success(discoveredPacks)
                : ManifestOperationResult<List<GitCachedPack>>.Failure(
                    packs.Error ?? "Unable to discover Git packs.",
                    packs.ErrorKind
                );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<List<GitCachedPack>>.Failure(exception.Message);
        }
        finally
        {
            GitTemporaryWorkspace.Delete(fileSystem, workspace);
        }
    }

    private async Task<ManifestOperationResult<List<GitCachedPack>>?> PrepareWorkspaceAsync(
        ProjectConfiguration.GitSource source,
        string resolvedCommit,
        string workspace,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        fileSystem.Directory.CreateDirectory(workspace);
        var commands = new[]
        {
            new[] { "init", "--quiet", workspace },
            ["-C", workspace, "remote", "add", "origin", source.Url],
            ["-C", workspace, "fetch", "--depth=1", "--filter=blob:none", "origin", resolvedCommit],
        };
        foreach (var command in commands)
        {
            var result = await processRunner.RunAsync(command, timeout, cancellationToken);
            if (!result.IsSuccess)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    result.Error ?? "Unable to prepare the Git discovery workspace."
                );
            }
        }

        return null;
    }

    private Task<ManifestOperationResult<GitProcessOutput>> ListManifestPathsAsync(
        ProjectConfiguration.GitSource source,
        string resolvedCommit,
        string workspace,
        TimeSpan timeout,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    ) =>
        processRunner.RunBoundedRecordsAsync(
            [
                "-C",
                workspace,
                "ls-tree",
                "-r",
                "-z",
                "--name-only",
                resolvedCommit,
                "--",
                source.Path ?? ".",
            ],
            timeout,
            resourceLimits.FilesPerGraph,
            "Git pack manifest enumeration",
            0,
            MaximumEnumerationOutputBytes,
            MaximumEnumerationRecordBytes,
            cancellationToken
        );

    private async Task<ManifestOperationResult<List<GitCachedPack>>> DiscoverPacksAsync(
        ProjectConfiguration.GitSource source,
        string resolvedCommit,
        string workspace,
        TimeSpan timeout,
        IReadOnlyList<string> manifestPaths,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    )
    {
        var packs = new List<GitCachedPack>();
        long catalogManifestBytes = 0;
        foreach (var path in manifestPaths.Where(IsManifestPath))
        {
            var manifestResult = await processRunner.RunBoundedAsync(
                ["-C", workspace, "show", $"{resolvedCommit}:{path}"],
                timeout,
                resourceLimits.PackManifestBytes,
                "Git pack manifest",
                cancellationToken
            );
            if (manifestResult.ErrorKind == ManifestOperationErrorKind.ResourceLimitExceeded)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    manifestResult.Error ?? "Git pack manifest exceeds its configured byte limit.",
                    manifestResult.ErrorKind
                );
            }

            if (manifestResult.Value is not { } manifestOutput)
            {
                continue;
            }

            var listedFiles = await ListPackSnapshotRecordsAsync(
                GetPackPath(path),
                resolvedCommit,
                workspace,
                timeout,
                resourceLimits,
                cancellationToken
            );
            if (listedFiles.Value is not { } snapshotRecords)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    listedFiles.Error ?? "Unable to enumerate Git pack snapshot.",
                    listedFiles.ErrorKind
                );
            }

            var snapshotFiles = ParsePackSnapshotRecords(
                ParseRecords(snapshotRecords),
                GetPackPath(path),
                resourceLimits
            );
            if (snapshotFiles.Value is not { } sourceFiles)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    snapshotFiles.Error ?? "Git pack snapshot exceeds its configured limits.",
                    snapshotFiles.ErrorKind
                );
            }

            var manifest = await TryParseAsync(manifestOutput.StandardOutput, sourceFiles);
            if (manifest is null)
            {
                _console.Debug(
                    $"Ignoring invalid pack manifest '{path}' from Git source '{source.Url}'."
                );
            }
            else
            {
                catalogManifestBytes = checked(catalogManifestBytes + manifest.ManifestByteCount);
                if (catalogManifestBytes > CatalogManifestBudget.MaximumManifestBytes)
                {
                    return ManifestOperationResult<List<GitCachedPack>>.Failure(
                        $"Git pack catalog manifest content exceeds its {CatalogManifestBudget.MaximumManifestBytes} byte limit (at least {catalogManifestBytes} bytes observed).",
                        ManifestOperationErrorKind.ResourceLimitExceeded
                    );
                }

                packs.Add(manifest with { PackPath = GetPackPath(path) });
            }
        }

        return ManifestOperationResult<List<GitCachedPack>>.Success(packs);
    }

    private Task<ManifestOperationResult<GitProcessOutput>> ListPackSnapshotRecordsAsync(
        string packPath,
        string resolvedCommit,
        string workspace,
        TimeSpan timeout,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    ) =>
        processRunner.RunBoundedRecordsAsync(
            ["-C", workspace, "ls-tree", "-r", "-l", "-z", resolvedCommit, "--", packPath],
            timeout,
            resourceLimits.FilesPerPack,
            "Git pack snapshot enumeration",
            0,
            MaximumEnumerationOutputBytes,
            MaximumEnumerationRecordBytes,
            cancellationToken
        );

    private static string[] ParseRecords(GitProcessOutput output) =>
        [
            .. output
                .StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(record => record.Trim())
                .Where(record => record.Length > 0),
        ];

    private static ManifestOperationResult<List<string>> ParsePackSnapshotRecords(
        string[] records,
        string packPath,
        ResourceLimits resourceLimits
    )
    {
        var budget = new PackSnapshotBudget(packPath, resourceLimits);
        var files = new List<string>(records.Length);
        var prefix = string.Equals(packPath, ".", StringComparison.Ordinal)
            ? string.Empty
            : $"{packPath}/";
        foreach (var record in records)
        {
            var separator = record.IndexOf('\t', StringComparison.Ordinal);
            var metadata = separator < 0 ? string.Empty : record[..separator];
            var repositoryPath = separator < 0 ? record : record[(separator + 1)..];
            if (!repositoryPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                return ManifestOperationResult<List<string>>.Failure(
                    $"Git pack snapshot entry '{repositoryPath}' is outside pack '{packPath}'."
                );
            }

            var metadataParts = metadata.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var size = 0L;
            if (
                metadataParts.Length >= 4
                && !long.TryParse(
                    metadataParts[3],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out size
                )
            )
            {
                size = 0;
            }

            var relativePath = repositoryPath[prefix.Length..];
            var added = budget.AddFilePath(relativePath, size);
            if (!added.IsSuccess)
            {
                return ManifestOperationResult<List<string>>.Failure(
                    added.Error ?? "Git pack snapshot exceeds its configured limits.",
                    added.ErrorKind
                );
            }

            files.Add(relativePath);
        }

        return ManifestOperationResult<List<string>>.Success(files);
    }

    private static List<CatalogPack> CreateCatalog(
        ProjectConfiguration.GitSource source,
        int sourceOrder,
        string resolvedCommit,
        IReadOnlyList<GitCachedPack> packs
    ) =>
        [
            .. packs.Select(pack =>
            {
                if (!NuGetVersion.TryParse(pack.Version, out var version))
                {
                    throw new InvalidOperationException(
                        $"Git pack '{pack.Id}' has an invalid cached version '{pack.Version}'."
                    );
                }

                return new CatalogPack(
                    source.Url,
                    pack.PackPath,
                    sourceOrder,
                    pack.Manifest,
                    version,
                    source.Name,
                    ConfiguredSourceIdentity.Create(source),
                    new GitSourceProvenance
                    {
                        Url = source.Url,
                        Ref = source.Ref,
                        Path = source.Path,
                        ResolvedCommit = resolvedCommit,
                    },
                    pack.PackPath,
                    ManifestByteCount: pack.ManifestByteCount
                );
            }),
        ];

    private static async Task<GitCachedPack?> TryParseAsync(
        string contents,
        IReadOnlyCollection<string> sourceFiles
    )
    {
        try
        {
            var manifest = _deserializer.Deserialize<PackManifest>(contents);
            if (manifest is null || !NuGetVersion.TryParse(manifest.Version, out _))
            {
                return null;
            }

            manifest = PackManifestPathNormalizer.Normalize(manifest);

            return (await PackManifestValidator.ValidateAsync(manifest, sourceFiles)).Count is 0
                ? new GitCachedPack
                {
                    Id = manifest.Id,
                    Version = manifest.Version,
                    Manifest = manifest,
                    ManifestByteCount = System.Text.Encoding.UTF8.GetByteCount(contents),
                    PackPath = string.Empty,
                }
                : null;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static string GetPackPath(string manifestPath)
    {
        var separator = manifestPath.LastIndexOf('/');
        return separator < 0 ? "." : manifestPath[..separator];
    }

    private static bool IsManifestPath(string path) =>
        string.Equals(path, "pack.yml", StringComparison.Ordinal)
        || path.EndsWith("/pack.yml", StringComparison.Ordinal);

    private static bool IsSafeRepositoryPath(string? path) =>
        path is null
        || (
            !Path.IsPathRooted(path)
            && path.Split(['/', '\\'])
                .All(segment => !string.Equals(segment, "..", StringComparison.Ordinal))
        );
}
