using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
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
    CliConsole console
)
{
    private const int DefaultTimeoutSeconds = 300;

    private static readonly IDeserializer _deserializer = new StaticDeserializerBuilder(
        new LunapackYamlContext()
    )
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly CliConsole _console = console;

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectConfiguration.GitSource source,
        int sourceOrder,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsSafeRepositoryPath(source.Path))
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                "Git source paths must be repository-relative and must not contain '..'."
            );
        }

        var loadedCache = cache.Load(projectDirectory, source);
        var cachedEntry = loadedCache.Value;
        var resolution = await refResolver.ResolveAsync(
            source,
            cachedEntry?.DefaultBranch,
            cancellationToken
        );
        if (resolution.Value is not { } resolvedSource)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                resolution.Error ?? "Unable to resolve Git source."
            );
        }

        if (
            cachedEntry is { } entry
            && string.Equals(
                entry.ResolvedCommit,
                resolvedSource.ResolvedCommit,
                StringComparison.Ordinal
            )
        )
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(
                CreateCatalog(source, sourceOrder, resolvedSource.ResolvedCommit, entry.Packs)
            );
        }

        var discovered = await DiscoverAsync(
            source,
            resolvedSource.ResolvedCommit,
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
        var saved = cache.Save(projectDirectory, newEntry);
        return saved.IsSuccess
            ? ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(
                CreateCatalog(source, sourceOrder, resolvedSource.ResolvedCommit, packs)
            )
            : ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                saved.Error ?? "Unable to persist Git source cache."
            );
    }

    public ManifestOperationResult<IReadOnlyList<CatalogPack>> BrowseCached(
        string projectDirectory,
        ProjectConfiguration.GitSource source,
        int sourceOrder
    )
    {
        var loadedCache = cache.Load(projectDirectory, source);
        return loadedCache.Value is { } entry
            ? ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(
                CreateCatalog(source, sourceOrder, entry.ResolvedCommit, entry.Packs)
            )
            : ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success([]);
    }

    private async Task<ManifestOperationResult<List<GitCachedPack>>> DiscoverAsync(
        ProjectConfiguration.GitSource source,
        string resolvedCommit,
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

            var listed = await ListRepositoryPathsAsync(
                source,
                resolvedCommit,
                workspace,
                timeout,
                cancellationToken
            );
            if (listed.Value is not { } paths)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(
                    listed.Error ?? "Unable to list Git repository paths."
                );
            }

            var repositoryPaths = ParseRepositoryPaths(paths);
            var packs = await DiscoverPacksAsync(
                source,
                resolvedCommit,
                workspace,
                timeout,
                repositoryPaths,
                cancellationToken
            );
            return ManifestOperationResult<List<GitCachedPack>>.Success(packs);
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

    private Task<ManifestOperationResult<GitProcessOutput>> ListRepositoryPathsAsync(
        ProjectConfiguration.GitSource source,
        string resolvedCommit,
        string workspace,
        TimeSpan timeout,
        CancellationToken cancellationToken
    ) =>
        processRunner.RunAsync(
            [
                "-C",
                workspace,
                "ls-tree",
                "-r",
                "--name-only",
                resolvedCommit,
                "--",
                source.Path ?? ".",
            ],
            timeout,
            cancellationToken
        );

    private static string[] ParseRepositoryPaths(GitProcessOutput paths) =>
        paths.StandardOutput.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

    private async Task<List<GitCachedPack>> DiscoverPacksAsync(
        ProjectConfiguration.GitSource source,
        string resolvedCommit,
        string workspace,
        TimeSpan timeout,
        IReadOnlyList<string> repositoryPaths,
        CancellationToken cancellationToken
    )
    {
        var packs = new List<GitCachedPack>();
        foreach (var path in repositoryPaths.Where(IsManifestPath))
        {
            var manifestResult = await processRunner.RunAsync(
                ["-C", workspace, "show", $"{resolvedCommit}:{path}"],
                timeout,
                cancellationToken
            );
            if (manifestResult.Value is not { } manifestOutput)
            {
                continue;
            }

            var manifest = await TryParseAsync(
                manifestOutput.StandardOutput,
                GetPackSourceFiles(repositoryPaths, GetPackPath(path))
            );
            if (manifest is null)
            {
                _console.Debug(
                    $"Ignoring invalid pack manifest '{path}' from Git source '{source.Url}'."
                );
            }
            else
            {
                packs.Add(manifest with { PackPath = GetPackPath(path) });
            }
        }

        return packs;
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
                    pack.PackPath
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
