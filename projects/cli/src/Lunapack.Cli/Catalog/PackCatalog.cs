using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;
using NuGet.Versioning;

namespace Lunapack.Cli.Catalog;

internal sealed class PackCatalog(
    IFileSystem fileSystem,
    CliConsole console,
    IGitProcessRunner? processRunner = null
)
{
    internal const int MaximumVersionCount = 10;

    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly CliConsole _console = console;
    private readonly GitPackDiscovery _gitPackDiscovery = new(
        fileSystem,
        processRunner ?? new GitProcessRunner(),
        new GitRefResolver(processRunner ?? new GitProcessRunner()),
        new GitSourceCache(fileSystem),
        console
    );
    private readonly LocalPackDiscovery _localPackDiscovery = new(fileSystem, console);

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectManifest manifest
    ) =>
        await BrowseAsync(
            projectDirectory,
            [
                .. manifest.Sources.Select(
                    (source, index) =>
                        new ConfiguredSource($"source-{index}", source.Type, source.Path)
                ),
            ]
        );

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectConfiguration configuration
    )
    {
        _console.Debug($"Browsing {configuration.Sources.Count} configured pack sources");
        var catalog = new List<CatalogPack>();
        for (var sourceOrder = 0; sourceOrder < configuration.Sources.Count; sourceOrder++)
        {
            var sourceCatalog = configuration.Sources[sourceOrder] switch
            {
                ProjectConfiguration.LocalSource localSource =>
                    await _localPackDiscovery.BrowseAsync(
                        _fileSystem.Path.GetFullPath(localSource.Path, projectDirectory),
                        sourceOrder,
                        localSource.Name,
                        ConfiguredSourceIdentity.Create(localSource)
                    ),
                ProjectConfiguration.GitSource gitSource => await _gitPackDiscovery.BrowseAsync(
                    projectDirectory,
                    gitSource,
                    sourceOrder
                ),
                _ => ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    "Pack source type is unsupported."
                ),
            };
            if (sourceCatalog.Value is not { } sourcePacks)
            {
                return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    sourceCatalog.Error ?? "Pack source returned no catalog."
                );
            }

            catalog.AddRange(sourcePacks);
        }

        return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(catalog);
    }

    private async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        IReadOnlyList<ConfiguredSource> sources
    )
    {
        var catalog = new List<CatalogPack>();
        for (var sourceOrder = 0; sourceOrder < sources.Count; sourceOrder++)
        {
            var source = sources[sourceOrder];
            var sourcePath = _fileSystem.Path.GetFullPath(source.Path, projectDirectory);
            var sourceIdentity = ConfiguredSourceIdentity.CreateLocal(source.Path);
            var sourceCatalog = source.Type switch
            {
                "local" => await _localPackDiscovery.BrowseAsync(
                    sourcePath,
                    sourceOrder,
                    source.Name,
                    sourceIdentity
                ),
                _ => ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    $"Pack source type '{source.Type}' is unsupported."
                ),
            };
            if (!sourceCatalog.IsSuccess)
            {
                return sourceCatalog;
            }

            if (sourceCatalog.Value is not { } sourcePacks)
            {
                return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    "Pack source returned no catalog."
                );
            }

            catalog.AddRange(sourcePacks);
        }

        return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(catalog);
    }

    public static IReadOnlyList<CatalogPack> GetLatest(IReadOnlyList<CatalogPack> catalog) =>
        GetRecentReleases(catalog, 1)
            .OrderBy(pack => pack.Manifest.Id, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<CatalogPack> GetRecentReleases(
        IReadOnlyList<CatalogPack> catalog,
        int versionCount
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(versionCount, 1);

        return catalog
            .GroupBy(pack => pack.Manifest.Id, StringComparer.Ordinal)
            .SelectMany(group =>
                group
                    .GroupBy(pack => pack.Version)
                    .Select(version => SelectPreferred([.. version], compareVersions: false))
                    .OrderByDescending(pack => pack.Version, VersionComparer.VersionRelease)
                    .Take(versionCount)
            )
            .ToList();
    }

    public static IReadOnlyList<CatalogPack> Search(
        IReadOnlyList<CatalogPack> catalog,
        string searchTerm
    )
    {
        var normalizedSearchTerm = searchTerm.ToUpperInvariant();
        var normalizedSearchTerms = searchTerm
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.ToUpperInvariant())
            .ToArray();

        return catalog
            .Select(pack => new CatalogSearchMatch(
                pack,
                GetRelevance(pack, normalizedSearchTerm, normalizedSearchTerms)
            ))
            .Where(match => match.Relevance is not null)
            .OrderBy(match => match.Relevance)
            .ThenBy(match => match.Pack.Manifest.Id, StringComparer.Ordinal)
            .ThenByDescending(match => match.Pack.Version, VersionComparer.VersionRelease)
            .ThenBy(match => match.Pack.SourceOrder)
            .ThenBy(match => match.Pack.PackDirectory, StringComparer.Ordinal)
            .Select(match => match.Pack)
            .ToList();
    }

    public async Task<ManifestOperationResult<DiscoveredPack>> ResolveAsync(
        string projectDirectory,
        ProjectManifest manifest,
        string packId,
        string? requestedVersion
    )
    {
        var catalog = await BrowseAsync(projectDirectory, manifest);
        if (catalog.Value is not { } catalogPacks)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                catalog.Error ?? "Unable to browse pack sources."
            );
        }

        return ResolveFromCatalog(catalogPacks, packId, requestedVersion);
    }

    public async Task<ManifestOperationResult<DiscoveredPack>> ResolveAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string packId,
        string? requestedVersion
    )
    {
        var catalog = await BrowseAsync(projectDirectory, configuration);
        if (catalog.Value is not { } catalogPacks)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                catalog.Error ?? "Unable to browse pack sources."
            );
        }

        return ResolveFromCatalog(catalogPacks, packId, requestedVersion);
    }

    internal static ManifestOperationResult<DiscoveredPack> ResolveFromCatalog(
        IReadOnlyList<CatalogPack> catalog,
        string packId,
        string? requestedVersion
    )
    {
        var selected = SelectFromCatalog(catalog, packId, requestedVersion);
        return selected.Value is { } selectedPack
            ? ManifestOperationResult<DiscoveredPack>.Success(
                new DiscoveredPack(
                    selectedPack.SourcePath,
                    selectedPack.PackDirectory,
                    selectedPack.Manifest,
                    selectedPack.SourceName,
                    selectedPack.SourceIdentity,
                    selectedPack.GitSource,
                    selectedPack.RepositoryPath
                )
            )
            : ManifestOperationResult<DiscoveredPack>.Failure(
                selected.Error ?? $"Pack '{packId}' is unavailable.",
                ManifestOperationErrorKind.PackNotFound
            );
    }

    internal static ManifestOperationResult<CatalogPack> SelectFromCatalog(
        IReadOnlyList<CatalogPack> catalog,
        string packId,
        string? requestedVersion
    )
    {
        var candidates = catalog
            .Where(pack => string.Equals(pack.Manifest.Id, packId, StringComparison.Ordinal))
            .ToList();
        if (requestedVersion is not null)
        {
            if (!NuGetVersion.TryParse(requestedVersion, out var requestedNuGetVersion))
            {
                return ManifestOperationResult<CatalogPack>.Failure(
                    $"Version '{requestedVersion}' is not a valid semantic version."
                );
            }

            var selectedVersion = candidates
                .Where(pack =>
                    VersionComparer.VersionRelease.Compare(pack.Version, requestedNuGetVersion) == 0
                )
                .ToList();
            if (selectedVersion.Count == 0 && candidates.Count > 0)
            {
                var latestPack = SelectPreferred(candidates, compareVersions: true);
                return ManifestOperationResult<CatalogPack>.Failure(
                    $"Pack '{packId}' is unavailable at requested version '{requestedVersion}'. Did you mean latest version '{latestPack.Manifest.Version}'?"
                );
            }

            candidates = selectedVersion;
        }

        if (candidates.Count == 0)
        {
            return ManifestOperationResult<CatalogPack>.Failure($"Pack '{packId}' is unavailable.");
        }

        var selectedPack = SelectPreferred(candidates, compareVersions: requestedVersion is null);

        return ManifestOperationResult<CatalogPack>.Success(selectedPack);
    }

    private static int? GetRelevance(
        CatalogPack pack,
        string normalizedSearchTerm,
        IReadOnlyList<string> normalizedSearchTerms
    )
    {
        var normalizedId = pack.Manifest.Id.ToUpperInvariant();
        var normalizedDescription = pack.Manifest.Description?.ToUpperInvariant();
        var normalizedTags = pack.Manifest.Tags.Select(tag => tag.ToUpperInvariant()).ToList();
        if (string.Equals(normalizedId, normalizedSearchTerm, StringComparison.Ordinal))
        {
            return 0;
        }

        if (normalizedId.StartsWith(normalizedSearchTerm, StringComparison.Ordinal))
        {
            return 1;
        }

        if (normalizedId.Contains(normalizedSearchTerm, StringComparison.Ordinal))
        {
            return 2;
        }

        if (
            normalizedDescription?.Contains(normalizedSearchTerm, StringComparison.Ordinal) is true
            || normalizedTags.Any(tag =>
                tag.Contains(normalizedSearchTerm, StringComparison.Ordinal)
            )
        )
        {
            return 3;
        }

        return normalizedSearchTerms.All(term =>
            normalizedId.Contains(term, StringComparison.Ordinal)
            || normalizedDescription?.Contains(term, StringComparison.Ordinal) is true
            || normalizedTags.Any(tag => tag.Contains(term, StringComparison.Ordinal))
        )
            ? 4
            : null;
    }

    private static CatalogPack SelectPreferred(List<CatalogPack> candidates, bool compareVersions)
    {
        var selectedPack = candidates[0];
        foreach (var candidate in candidates.Skip(1))
        {
            if (IsPreferred(candidate, selectedPack, compareVersions))
            {
                selectedPack = candidate;
            }
        }

        return selectedPack;
    }

    private static bool IsPreferred(
        CatalogPack candidate,
        CatalogPack selectedPack,
        bool compareVersions
    )
    {
        if (compareVersions)
        {
            var versionComparison = VersionComparer.VersionRelease.Compare(
                candidate.Version,
                selectedPack.Version
            );
            if (versionComparison != 0)
            {
                return versionComparison > 0;
            }
        }

        var sourceComparison = candidate.SourceOrder.CompareTo(selectedPack.SourceOrder);
        if (sourceComparison != 0)
        {
            return sourceComparison < 0;
        }

        return string.Compare(
                candidate.PackDirectory,
                selectedPack.PackDirectory,
                StringComparison.Ordinal
            ) < 0;
    }

    private sealed record CatalogSearchMatch(CatalogPack Pack, int? Relevance);

    private sealed record ConfiguredSource(string Name, string Type, string Path);
}
