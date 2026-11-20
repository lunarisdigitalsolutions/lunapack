using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Catalog;
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
    CliConsole console,
    GitTransportPolicy transportPolicy
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

    public Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectConfiguration.GitSource source,
        int sourceOrder,
        CancellationToken cancellationToken
    ) =>
        BrowseAsync(projectDirectory, source, sourceOrder, resourceLimits: null, cancellationToken);

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

        var authorization = await transportPolicy.AuthorizeAsync(
            projectDirectory,
            source.Url,
            source.Name
        );
        if (!authorization.IsSuccess)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                authorization.Error ?? $"Unable to authorize Git source '{source.Name}'."
            );
        }
        var transportMode = authorization.Value;

        var resolution = await refResolver.ResolveAsync(
            source,
            cachedDefaultBranch: null,
            cancellationToken,
            transportMode
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
            transportMode,
            cancellationToken
        );
        if (discovered.Value is not { } packs)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                discovered.Error ?? "Unable to discover Git packs."
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
        if (
            currentEntry.Value is { } cachedEntry
            && GitSourceCache.AreEquivalent(cachedEntry, newEntry)
        )
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
                saved.Error ?? "Unable to persist Git source cache."
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
            GitTransportMode.SecureOnly,
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
        GitTransportMode transportMode,
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
                transportMode,
                cancellationToken
            );
            if (preparationFailure is not null)
            {
                return preparationFailure;
            }

            var listed = await ListRepositoryPathsAsync(
                source,
                resolvedCommit,
                workspace,
                timeout,
                resourceLimits,
                transportMode,
                cancellationToken
            );
            if (listed.Value is not { } paths)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    GitSourceFailureFormatter.Format(
                        source.Name,
                        listed.Error,
                        "Unable to list Git repository paths."
                    )
                );
            }
            if (paths.StandardOutputTruncated)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    "Git repository path output was truncated; discovery cannot continue safely."
                );
            }

            var repositoryPaths = ParseRepositoryPaths(paths);
            var packs = await DiscoverPacksAsync(
                source,
                resolvedCommit,
                workspace,
                timeout,
                repositoryPaths,
                resourceLimits,
                transportMode,
                cancellationToken
            );
            return packs;
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
        GitTransportMode transportMode,
        CancellationToken cancellationToken
    )
    {
        fileSystem.Directory.CreateDirectory(workspace);
        var commands = new[]
        {
            (new[] { "init", "--quiet", workspace }, GitTransportMode.SecureOnly, (string?)null),
            (
                ["-C", workspace, "remote", "add", "--", "origin", source.Url],
                GitTransportMode.SecureOnly,
                null
            ),
            (
                new[]
                {
                    "-C",
                    workspace,
                    "fetch",
                    "--depth=1",
                    "--filter=blob:none",
                    "origin",
                    resolvedCommit,
                },
                transportMode,
                source.Url
            ),
        };
        foreach (var (command, commandTransportMode, repositoryUrl) in commands)
        {
            var result = await processRunner.RunAsync(
                command,
                timeout,
                commandTransportMode,
                repositoryUrl,
                cancellationToken
            );
            if (!result.IsSuccess)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    GitSourceFailureFormatter.Format(
                        source.Name,
                        result.Error,
                        "Unable to prepare the Git discovery workspace."
                    )
                );
            }
        }

        return null;
    }

    private Task<ManifestOperationResult<GitProcessOutput>> ListRepositoryPathsAsync(
        ProjectConfiguration.GitSource source,
        string resolvedCommit,
        string workspace,
        TimeSpan timeout,
        ResourceLimits resourceLimits,
        GitTransportMode transportMode,
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
            transportMode,
            source.Url,
            cancellationToken
        );

    private static string[] ParseRepositoryPaths(GitProcessOutput paths) =>
        paths.StandardOutput.Split(
            '\0',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

    private async Task<ManifestOperationResult<List<GitCachedPack>>> DiscoverPacksAsync(
        ProjectConfiguration.GitSource source,
        string resolvedCommit,
        string workspace,
        TimeSpan timeout,
        IReadOnlyList<string> repositoryPaths,
        ResourceLimits resourceLimits,
        GitTransportMode transportMode,
        CancellationToken cancellationToken
    )
    {
        var packs = new List<GitCachedPack>();
        long catalogManifestBytes = 0;
        foreach (var path in repositoryPaths.Where(IsManifestPath))
        {
            var manifestResult = await processRunner.RunBoundedAsync(
                ["-C", workspace, "show", $"{resolvedCommit}:{path}"],
                timeout,
                resourceLimits.PackManifestBytes,
                "Git pack manifest",
                transportMode,
                source.Url,
                cancellationToken
            );
            if (manifestResult.Value is not { } manifestOutput)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    GitSourceFailureFormatter.Format(
                        source.Name,
                        manifestResult.Error,
                        $"Unable to read Git source manifest '{path}' from '{source.Name}'."
                    )
                );
            }
            if (manifestOutput.StandardOutputTruncated)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    $"Git source manifest '{path}' was truncated.",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }

            var manifest = await TryParseAsync(
                manifestOutput.StandardOutput,
                GetPackSourceFiles(repositoryPaths, GetPackPath(path))
            );
            if (manifest is null)
            {
                _console.Debug(
                    $"Ignoring invalid pack manifest '{path}' from Git source '{source.Name}'."
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

    private static List<string> GetPackSourceFiles(
        IReadOnlyList<string> repositoryPaths,
        string packPath
    )
    {
        var prefix = string.Equals(packPath, ".", StringComparison.Ordinal)
            ? string.Empty
            : $"{packPath}/";
        return
        [
            .. repositoryPaths
                .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
                .Select(path => path[prefix.Length..]),
        ];
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
