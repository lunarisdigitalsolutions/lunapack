using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Sources.Oci;
using Lunapack.Cli.Trust;
using NuGet.Versioning;

namespace Lunapack.Cli.Catalog;

internal sealed class PackCatalog : IDisposable
{
    internal const int MaximumVersionCount = 10;

    private readonly IFileSystem _fileSystem;
    private readonly CliConsole _console;
    private readonly ConfiguredSourceIgnoreService _sourceIgnoreService;
    private readonly GitPackDiscovery _gitPackDiscovery;
    private readonly LocalPackDiscovery _localPackDiscovery;
    private readonly OciPackSource _ociPackSource;
    private readonly bool _ownsOciPackSource;
    private readonly IOperationSnapshotSecurity _operationSnapshotSecurity;

    public PackCatalog(
        IFileSystem fileSystem,
        CliConsole console,
        IGitProcessRunner? processRunner = null,
        UserSettingsStore? userSettingsStore = null,
        OciPackSource? ociPackSource = null,
        ConfiguredSourceIgnoreService? configuredSourceIgnoreService = null,
        IOperationSnapshotSecurity? operationSnapshotSecurity = null
    )
    {
        _fileSystem = fileSystem;
        _console = console;
        _operationSnapshotSecurity = operationSnapshotSecurity ?? new OperationSnapshotSecurity();
        _sourceIgnoreService =
            configuredSourceIgnoreService
            ?? new ConfiguredSourceIgnoreService(fileSystem, userSettingsStore);

        var effectiveProcessRunner = processRunner ?? new GitProcessRunner();
        _gitPackDiscovery = new GitPackDiscovery(
            fileSystem,
            effectiveProcessRunner,
            new GitRefResolver(effectiveProcessRunner),
            new GitSourceCache(fileSystem),
            console
        );
        _localPackDiscovery = new LocalPackDiscovery(fileSystem, console);
        _ownsOciPackSource = ociPackSource is null;
        _ociPackSource = ociPackSource ?? new OciPackSource(fileSystem, console);
    }

    public void Dispose()
    {
        if (_ownsOciPackSource)
        {
            _ociPackSource.Dispose();
        }
    }

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectManifest manifest
    ) =>
        await BrowseAsync(
            projectDirectory,
            new ProjectConfiguration
            {
                Sources =
                [
                    .. manifest.Sources.Select(
                        (source, index) =>
                            (ProjectConfiguration.Source)
                                new ProjectConfiguration.LocalSource
                                {
                                    Name = $"source-{index}",
                                    Path = source.Path,
                                }
                    ),
                ],
            }
        );

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectConfiguration configuration
    )
    {
        _console.Debug($"Browsing {configuration.Sources.Count} configured pack sources");
        var ignoreContextResult = await _sourceIgnoreService.LoadAsync(projectDirectory);
        if (ignoreContextResult.Value is not { } ignoreContext)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                ignoreContextResult.Error ?? "Unable to load local source ignore settings."
            );
        }

        return await BrowseConfiguredSourcesAsync(projectDirectory, configuration, ignoreContext);
    }

    private async Task<
        ManifestOperationResult<IReadOnlyList<CatalogPack>>
    > BrowseConfiguredSourcesAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ConfiguredSourceIgnoreService.SourceIgnoreContext ignoreContext
    )
    {
        var catalog = new List<CatalogPack>();
        var activeSourceCount = 0;
        var successfulSourceCount = 0;
        var failedSources = new List<string>();
        for (var sourceOrder = 0; sourceOrder < configuration.Sources.Count; sourceOrder++)
        {
            var source = configuration.Sources[sourceOrder];
            var ignoreResult = IsSourceIgnored(source, ignoreContext);
            if (!ignoreResult.IsSuccess)
            {
                return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    ignoreResult.Error ?? $"Unable to identify source '{source.Name}'."
                );
            }

            if (ignoreResult.Value)
            {
                _console.Info($"Skipping locally ignored source '{source.Name}'.");
                continue;
            }

            activeSourceCount++;
            var sourceCatalog = await BrowseSourceAsync(projectDirectory, source, sourceOrder);
            if (sourceCatalog.Value is not { } sourcePacks)
            {
                var error = sourceCatalog.Error ?? "Source returned no catalog.";
                failedSources.Add($"{source.Name}: {error}");
                _console.Warning($"Skipping unavailable pack source '{source.Name}': {error}");
                continue;
            }

            successfulSourceCount++;
            catalog.AddRange(sourcePacks);
        }

        if (activeSourceCount == 0 && configuration.Sources.Count > 0)
        {
            _console.Info("All configured pack sources are ignored locally.");
        }

        if (activeSourceCount > 0 && successfulSourceCount == 0)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                string.Join(Environment.NewLine, failedSources)
            );
        }

        if (failedSources.Count > 0)
        {
            _console.Warning(
                $"Pack catalog is incomplete; {failedSources.Count} configured source(s) were unavailable."
            );
        }

        return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(catalog);
    }

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseCachedAsync(
        string projectDirectory,
        ProjectConfiguration configuration
    )
    {
        var ignoreContextResult = await _sourceIgnoreService.LoadAsync(projectDirectory);
        if (ignoreContextResult.Value is not { } ignoreContext)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                ignoreContextResult.Error ?? "Unable to load local source ignore settings."
            );
        }

        return await BrowseCachedSourcesAsync(projectDirectory, configuration, ignoreContext);
    }

    private async Task<
        ManifestOperationResult<IReadOnlyList<CatalogPack>>
    > BrowseCachedSourcesAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ConfiguredSourceIgnoreService.SourceIgnoreContext ignoreContext
    )
    {
        var catalog = new List<CatalogPack>();
        var activeSourceCount = 0;
        var uncheckedOciSources = new List<string>();
        for (var sourceOrder = 0; sourceOrder < configuration.Sources.Count; sourceOrder++)
        {
            var source = configuration.Sources[sourceOrder];
            var ignoreResult = IsSourceIgnored(source, ignoreContext);
            if (!ignoreResult.IsSuccess)
            {
                return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    ignoreResult.Error ?? $"Unable to identify source '{source.Name}'."
                );
            }

            if (ignoreResult.Value)
            {
                _console.Info($"Skipping locally ignored source '{source.Name}'.");
                continue;
            }

            activeSourceCount++;
            var sourceCatalog = await BrowseCachedSourceAsync(
                projectDirectory,
                source,
                sourceOrder
            );
            if (sourceCatalog.Value is { } sourcePacks)
            {
                catalog.AddRange(sourcePacks);
            }
            else if (!sourceCatalog.IsSuccess)
            {
                if (source is ProjectConfiguration.OciSource)
                {
                    uncheckedOciSources.Add(
                        sourceCatalog.Error ?? $"OCI source '{source.Name}' was not checked."
                    );
                    _console.Warning(sourceCatalog.Error ?? "OCI source was not checked offline.");
                    continue;
                }

                return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    sourceCatalog.Error ?? "Unable to browse cached pack sources."
                );
            }
        }

        if (activeSourceCount == 0 && configuration.Sources.Count > 0)
        {
            _console.Info("All configured pack sources are ignored locally.");
        }

        if (catalog.Count == 0 && uncheckedOciSources.Count > 0)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                string.Join(Environment.NewLine, uncheckedOciSources)
            );
        }

        return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(catalog);
    }

    private Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseSourceAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        int sourceOrder
    ) =>
        source switch
        {
            ProjectConfiguration.LocalSource localSource => _localPackDiscovery.BrowseAsync(
                _fileSystem.Path.GetFullPath(localSource.Path, projectDirectory),
                sourceOrder,
                localSource.Name,
                ConfiguredSourceIdentity.Create(localSource)
            ),
            ProjectConfiguration.GitSource gitSource => _gitPackDiscovery.BrowseAsync(
                projectDirectory,
                gitSource,
                sourceOrder
            ),
            ProjectConfiguration.OciSource ociSource => _ociPackSource.BrowseAsync(
                projectDirectory,
                ociSource,
                sourceOrder
            ),
            _ => Task.FromResult(
                ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    "Pack source type is unsupported."
                )
            ),
        };

    private Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseCachedSourceAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        int sourceOrder
    ) =>
        source switch
        {
            ProjectConfiguration.LocalSource localSource => _localPackDiscovery.BrowseAsync(
                _fileSystem.Path.GetFullPath(localSource.Path, projectDirectory),
                sourceOrder,
                localSource.Name,
                ConfiguredSourceIdentity.Create(localSource)
            ),
            ProjectConfiguration.GitSource gitSource => Task.FromResult(
                _gitPackDiscovery.BrowseCached(projectDirectory, gitSource, sourceOrder)
            ),
            ProjectConfiguration.OciSource ociSource => Task.FromResult(
                OciPackSource.BrowseCached(ociSource)
            ),
            _ => Task.FromResult(ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success([])),
        };

    private ManifestOperationResult<bool> IsSourceIgnored(
        ProjectConfiguration.Source source,
        ConfiguredSourceIgnoreService.SourceIgnoreContext context
    ) => _sourceIgnoreService.IsIgnored(context, source);

    public static IReadOnlyList<CatalogPack> GetLatest(IReadOnlyList<CatalogPack> catalog) =>
        [
            .. GetRecentReleases(catalog, 1)
                .OrderBy(pack => pack.Manifest.Id, StringComparer.Ordinal),
        ];

    public static IReadOnlyList<CatalogPack> GetRecentReleases(
        IReadOnlyList<CatalogPack> catalog,
        int versionCount
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(versionCount, 1);

        return
        [
            .. catalog
                .GroupBy(pack => pack.Manifest.Id, StringComparer.Ordinal)
                .SelectMany(group =>
                    group
                        .GroupBy(pack => pack.Version)
                        .Select(version => SelectPreferred([.. version], compareVersions: false))
                        .OrderByDescending(pack => pack.Version, VersionComparer.VersionRelease)
                        .Take(versionCount)
                ),
        ];
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

        return
        [
            .. catalog
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
                .Select(match => match.Pack),
        ];
    }

    public async Task<ManifestOperationResult<DiscoveredPack>> ResolveAsync(
        string projectDirectory,
        ProjectManifest manifest,
        string packId,
        string? requestedVersion
    )
    {
        var configuration = new ProjectConfiguration
        {
            Sources =
            [
                .. manifest.Sources.Select(
                    (source, index) =>
                        (ProjectConfiguration.Source)
                            new ProjectConfiguration.LocalSource
                            {
                                Name = $"source-{index}",
                                Path = source.Path,
                            }
                ),
            ],
        };
        return await ResolveAsync(projectDirectory, configuration, packId, requestedVersion);
    }

    public async Task<ManifestOperationResult<DiscoveredPack>> ResolveAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string packId,
        string? requestedVersion
    )
    {
        var discovered = await DiscoverResolutionCandidatesAsync(
            projectDirectory,
            configuration,
            packId,
            requestedVersion
        );
        if (discovered.Value is not { } candidates)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                discovered.Error ?? "Unable to browse pack sources."
            );
        }

        var selected = SelectFromCatalog(candidates.Packs, packId, requestedVersion);
        if (selected.Value is not { } selectedPack)
        {
            return candidates.Failures.Count == 0
                ? ResolveFromCatalog(candidates.Packs, packId, requestedVersion)
                : ManifestOperationResult<DiscoveredPack>.Failure(
                    $"Resolution for pack '{packId}' is incomplete because one or more sources were unavailable: {string.Join("; ", candidates.Failures)}"
                );
        }

        if (candidates.Failures.Count > 0)
        {
            _console.Warning(
                $"Resolution for pack '{packId}' is incomplete; using the best available candidate from accessible sources."
            );
        }

        return ManifestOperationResult<DiscoveredPack>.Success(
            new DiscoveredPack(
                selectedPack.SourcePath,
                selectedPack.PackDirectory,
                selectedPack.Manifest,
                selectedPack.SourceName,
                selectedPack.SourceIdentity,
                selectedPack.GitSource,
                selectedPack.RepositoryPath,
                selectedPack.OciSource
            )
            {
                SourceSelection = GetSourceSelection(candidates.Packs, selectedPack),
            }
        );
    }

    public async Task<ManifestOperationResult<DiscoveredPack>> ResolveLockedOciAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ProjectLockFile.ResolvedPack lockedPack,
        CancellationToken cancellationToken = default
    )
    {
        var sourceResult = await ResolveLockedOciSourceAsync(
            projectDirectory,
            configuration,
            lockedPack
        );
        if (sourceResult.Value is not { } lockedSource)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                sourceResult.Error ?? $"Unable to resolve locked OCI pack '{lockedPack.Id}'."
            );
        }

        var workspaceResult = OciTemporaryWorkspace.Create(_fileSystem, _operationSnapshotSecurity);
        if (workspaceResult.Value is not { } workspace)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                workspaceResult.Error ?? "Unable to create an OCI workspace."
            );
        }

        using (workspace)
        {
            var release = await _ociPackSource.FetchReleaseAsync(
                projectDirectory,
                lockedSource.Source,
                lockedSource.Provenance.Repository,
                lockedPack.Id,
                lockedSource.Provenance.Tag,
                workspace.Path,
                lockedSource.Provenance.ManifestDigest,
                lockedSource.Provenance.ContentDigest,
                cancellationToken
            );
            if (release.Value is not { } lockedRelease)
            {
                return ManifestOperationResult<DiscoveredPack>.Failure(
                    release.Error
                        ?? $"Locked OCI release '{lockedPack.Id}@{lockedPack.Version}' is unavailable.",
                    release.ErrorKind
                );
            }

            return ManifestOperationResult<DiscoveredPack>.Success(
                new DiscoveredPack(
                    string.Empty,
                    string.Empty,
                    lockedRelease.Manifest,
                    lockedSource.Source.Name,
                    lockedSource.Identity,
                    RepositoryPath: lockedSource.Provenance.Repository,
                    OciSource: lockedRelease.Provenance
                )
            );
        }
    }

    public async Task<ManifestOperationResult<DiscoveredPack>> ResolveLockedAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ProjectLockFile.ResolvedPack lockedPack
    )
    {
        if (lockedPack.OciSource is not null)
        {
            return await ResolveLockedOciAsync(projectDirectory, configuration, lockedPack);
        }

        if (lockedPack.SourceIdentity is not { } sourceIdentity)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                $"Pack '{lockedPack.Id}@{lockedPack.Version}' does not contain source identity lock data."
            );
        }

        var source = configuration.Sources.FirstOrDefault(candidate =>
            ConfiguredSourceIdentity.Create(candidate) == sourceIdentity
        );
        if (source is null)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                $"Pack '{lockedPack.Id}@{lockedPack.Version}' is locked to a source that is not configured."
            );
        }

        var ignoreContextResult = await _sourceIgnoreService.LoadAsync(projectDirectory);
        if (ignoreContextResult.Value is not { } ignoreContext)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                ignoreContextResult.Error ?? "Unable to load local source ignore settings."
            );
        }

        var ignoreResult = IsSourceIgnored(source, ignoreContext);
        if (!ignoreResult.IsSuccess)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                ignoreResult.Error ?? $"Unable to identify source '{source.Name}'."
            );
        }

        if (ignoreResult.Value)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                $"Pack '{lockedPack.Id}@{lockedPack.Version}' requires source '{source.Name}', which is ignored locally. Run 'luna sources unignore {source.Name}' to continue."
            );
        }

        var sourceOrder = configuration.Sources.FindIndex(candidate =>
            ConfiguredSourceIdentity.Create(candidate) == sourceIdentity
        );
        var sourceCatalog = await BrowseSourceAsync(projectDirectory, source, sourceOrder);
        if (sourceCatalog.Value is not { } candidates)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                $"Locked pack '{lockedPack.Id}@{lockedPack.Version}' cannot be read from source '{source.Name}': {sourceCatalog.Error ?? "source unavailable"}.",
                sourceCatalog.ErrorKind
            );
        }

        var resolved = ResolveFromCatalog(candidates, lockedPack.Id, lockedPack.Version);
        return resolved.Value is { } pack
            ? ManifestOperationResult<DiscoveredPack>.Success(pack)
            : ManifestOperationResult<DiscoveredPack>.Failure(
                $"Locked pack '{lockedPack.Id}@{lockedPack.Version}' is unavailable from source '{source.Name}'.",
                resolved.ErrorKind
            );
    }

    public async Task<ManifestOperationResult<CatalogPack?>> ResolveLockedOciTagAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ProjectLockFile.ResolvedPack lockedPack,
        CancellationToken cancellationToken = default
    )
    {
        var sourceResult = await ResolveLockedOciSourceAsync(
            projectDirectory,
            configuration,
            lockedPack
        );
        if (sourceResult.Value is not { } lockedSource)
        {
            return ManifestOperationResult<CatalogPack?>.Failure(
                sourceResult.Error ?? $"Unable to resolve locked OCI pack '{lockedPack.Id}'."
            );
        }

        var sourceOrder = configuration.Sources.FindIndex(candidate =>
            ConfiguredSourceIdentity.Create(candidate) == lockedSource.Identity
        );
        if (sourceOrder < 0)
        {
            return ManifestOperationResult<CatalogPack?>.Failure(
                $"Pack '{lockedPack.Id}@{lockedPack.Version}' is locked to an unconfigured OCI source."
            );
        }

        var candidates = await _ociPackSource.ResolveCandidatesAsync(
            projectDirectory,
            lockedSource.Source,
            sourceOrder,
            lockedPack.Id,
            lockedPack.Version,
            cancellationToken
        );
        if (candidates.Value is not { } resolvedCandidates)
        {
            return ManifestOperationResult<CatalogPack?>.Failure(
                candidates.Error
                    ?? $"Unable to check OCI release '{lockedPack.Id}@{lockedPack.Version}'.",
                candidates.ErrorKind
            );
        }

        return ManifestOperationResult<CatalogPack?>.Success(
            resolvedCandidates.FirstOrDefault(candidate =>
                candidate.SourceIdentity == lockedSource.Identity
            )
        );
    }

    private async Task<ManifestOperationResult<LockedOciSource>> ResolveLockedOciSourceAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ProjectLockFile.ResolvedPack lockedPack
    )
    {
        if (
            lockedPack.SourceIdentity is not { } sourceIdentity
            || lockedPack.OciSource is not { } provenance
        )
        {
            return ManifestOperationResult<LockedOciSource>.Failure(
                $"Pack '{lockedPack.Id}@{lockedPack.Version}' does not contain complete OCI lock provenance."
            );
        }

        var source = configuration
            .Sources.OfType<ProjectConfiguration.OciSource>()
            .SingleOrDefault(candidate =>
                ConfiguredSourceIdentity.Create(candidate) == sourceIdentity
            );
        if (source is null)
        {
            return ManifestOperationResult<LockedOciSource>.Failure(
                $"Pack '{lockedPack.Id}@{lockedPack.Version}' is locked to OCI source '{lockedPack.SourceName ?? "unknown"}', which is not configured."
            );
        }

        var ignoreContextResult = await _sourceIgnoreService.LoadAsync(projectDirectory);
        if (ignoreContextResult.Value is not { } ignoreContext)
        {
            return ManifestOperationResult<LockedOciSource>.Failure(
                ignoreContextResult.Error ?? "Unable to load local source ignore settings."
            );
        }

        var ignoreResult = IsSourceIgnored(source, ignoreContext);
        if (!ignoreResult.IsSuccess)
        {
            return ManifestOperationResult<LockedOciSource>.Failure(
                ignoreResult.Error ?? $"Unable to identify source '{source.Name}'."
            );
        }

        if (ignoreResult.Value)
        {
            return ManifestOperationResult<LockedOciSource>.Failure(
                $"Pack '{lockedPack.Id}@{lockedPack.Version}' requires OCI source '{source.Name}', which is ignored locally. Run 'luna sources unignore {source.Name}' to continue."
            );
        }

        var location = SourceIdentityNormalizer.NormalizeOciLocation(source.Url, source.Path);
        var repository = OciRepositoryTagMapper.CreateRepository(source, lockedPack.Id);
        var tag = OciRepositoryTagMapper.CreateTag(lockedPack.Version);
        if (
            !location.IsSuccess
            || !repository.IsSuccess
            || !tag.IsSuccess
            || !string.Equals(location.Value.Url, provenance.RegistryUrl, StringComparison.Ordinal)
            || !string.Equals(repository.Value, provenance.Repository, StringComparison.Ordinal)
            || !string.Equals(tag.Value, provenance.Tag, StringComparison.Ordinal)
        )
        {
            return ManifestOperationResult<LockedOciSource>.Failure(
                $"OCI lock provenance for pack '{lockedPack.Id}@{lockedPack.Version}' does not match configured source '{source.Name}'."
            );
        }

        return ManifestOperationResult<LockedOciSource>.Success(
            new LockedOciSource(source, sourceIdentity, provenance)
        );
    }

    private sealed record LockedOciSource(
        ProjectConfiguration.OciSource Source,
        ConfiguredSourceIdentity Identity,
        OciSourceProvenance Provenance
    );

    private async Task<
        ManifestOperationResult<ResolutionCandidates>
    > DiscoverResolutionCandidatesAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string packId,
        string? requestedVersion
    )
    {
        var ignoreContextResult = await _sourceIgnoreService.LoadAsync(projectDirectory);
        if (ignoreContextResult.Value is not { } ignoreContext)
        {
            return ManifestOperationResult<ResolutionCandidates>.Failure(
                ignoreContextResult.Error ?? "Unable to load local source ignore settings."
            );
        }

        var packs = new List<CatalogPack>();
        var failures = new List<string>();
        for (var sourceOrder = 0; sourceOrder < configuration.Sources.Count; sourceOrder++)
        {
            var source = configuration.Sources[sourceOrder];
            var ignoreResult = IsSourceIgnored(source, ignoreContext);
            if (!ignoreResult.IsSuccess)
            {
                return ManifestOperationResult<ResolutionCandidates>.Failure(
                    ignoreResult.Error ?? $"Unable to identify source '{source.Name}'."
                );
            }

            if (ignoreResult.Value)
            {
                _console.Info($"Skipping locally ignored source '{source.Name}'.");
                continue;
            }

            var sourcePacks = source switch
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
                ProjectConfiguration.OciSource ociSource =>
                    await _ociPackSource.ResolveCandidatesAsync(
                        projectDirectory,
                        ociSource,
                        sourceOrder,
                        packId,
                        requestedVersion
                    ),
                _ => ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    "Pack source type is unsupported."
                ),
            };
            if (sourcePacks.Value is { } candidates)
            {
                packs.AddRange(candidates);
            }
            else
            {
                var error = sourcePacks.Error ?? "Source returned no resolution candidates.";
                failures.Add($"{source.Name}: {error}");
                _console.Warning($"Skipping unavailable pack source '{source.Name}': {error}");
            }
        }

        return ManifestOperationResult<ResolutionCandidates>.Success(
            new ResolutionCandidates(packs, failures)
        );
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
                    selectedPack.RepositoryPath,
                    selectedPack.OciSource
                )
                {
                    SourceSelection = GetSourceSelection(catalog, selectedPack),
                }
            )
            : ManifestOperationResult<DiscoveredPack>.Failure(
                selected.Error ?? $"Pack '{packId}' is unavailable.",
                ManifestOperationErrorKind.PackNotFound
            );
    }

    internal static PackSourceSelection? GetSourceSelection(
        IReadOnlyList<CatalogPack> catalog,
        CatalogPack selectedPack
    )
    {
        var matchingSourceCount = catalog
            .Where(candidate =>
                string.Equals(
                    candidate.Manifest.Id,
                    selectedPack.Manifest.Id,
                    StringComparison.Ordinal
                )
                && VersionComparer.VersionRelease.Compare(candidate.Version, selectedPack.Version)
                    == 0
            )
            .Select(candidate => candidate.SourceIdentity)
            .Distinct()
            .Take(2)
            .Count();
        return matchingSourceCount > 1
            ? new PackSourceSelection(
                selectedPack.Manifest.Id,
                selectedPack.SourceName,
                selectedPack.SourceIdentity.Type
            )
            : null;
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

        var matchesDescriptionOrTag =
            normalizedDescription?.Contains(normalizedSearchTerm, StringComparison.Ordinal) is true
            || normalizedTags.Any(tag =>
                tag.Contains(normalizedSearchTerm, StringComparison.Ordinal)
            );
        if (matchesDescriptionOrTag)
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

    private sealed record ResolutionCandidates(
        IReadOnlyList<CatalogPack> Packs,
        IReadOnlyList<string> Failures
    );
}
