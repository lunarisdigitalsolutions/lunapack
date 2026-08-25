using System.IO.Abstractions;
using NuGet.Versioning;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli;

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
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly CliConsole _console = console;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Browse coordinates cache validation, ref resolution, discovery, and persistence."
    )]
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Discovery owns the temporary workspace lifecycle and sequential Git operations."
    )]
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
            fileSystem.Directory.CreateDirectory(workspace);
            foreach (
                var command in new[]
                {
                    new[] { "init", "--quiet", workspace },
                    ["-C", workspace, "remote", "add", "origin", source.Url],
                    [
                        "-C",
                        workspace,
                        "fetch",
                        "--depth=1",
                        "--filter=blob:none",
                        "origin",
                        resolvedCommit,
                    ],
                }
            )
            {
                var result = await processRunner.RunAsync(command, timeout, cancellationToken);
                if (!result.IsSuccess)
                {
                    return ManifestOperationResult<List<GitCachedPack>>.Failure(result.Error!);
                }
            }

            var listed = await processRunner.RunAsync(
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
            if (listed.Value is not { } paths)
            {
                return ManifestOperationResult<List<GitCachedPack>>.Failure(listed.Error!);
            }

            var repositoryPaths = paths.StandardOutput.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
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

    private static List<CatalogPack> CreateCatalog(
        ProjectConfiguration.GitSource source,
        int sourceOrder,
        string resolvedCommit,
        IReadOnlyList<GitCachedPack> packs
    ) =>
        packs
            .Select(pack =>
            {
                _ = NuGetVersion.TryParse(pack.Version, out var version);
                return new CatalogPack(
                    source.Url,
                    pack.PackPath,
                    sourceOrder,
                    pack.Manifest,
                    version!,
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
            })
            .ToList();

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
        return repositoryPaths
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => path[prefix.Length..])
            .ToList();
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
