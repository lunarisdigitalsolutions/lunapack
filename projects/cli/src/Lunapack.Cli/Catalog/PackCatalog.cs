using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
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

    private enum ResolutionEligibility
    {
        AllCatalogReleases,
        DirectlyInstallable,
    }

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
        IOperationSnapshotSecurity? operationSnapshotSecurity = null,
        GitTransportPolicy? transportPolicy = null
    )
    {
        _fileSystem = fileSystem;
        _console = console;
        _operationSnapshotSecurity = operationSnapshotSecurity ?? new OperationSnapshotSecurity();
        var effectiveUserSettingsStore = userSettingsStore ?? new UserSettingsStore(fileSystem);
        _sourceIgnoreService =
            configuredSourceIgnoreService
            ?? new ConfiguredSourceIgnoreService(fileSystem, userSettingsStore);

        var effectiveProcessRunner = processRunner ?? new GitProcessRunner();
        var effectiveTransportPolicy =
            transportPolicy
            ?? new GitTransportPolicy(
                new CliPreferenceService(
                    fileSystem,
                    new ProjectStateStore(fileSystem),
                    effectiveUserSettingsStore
                ),
                console
            );
        _gitPackDiscovery = new GitPackDiscovery(
            fileSystem,
            effectiveProcessRunner,
            new GitRefResolver(effectiveProcessRunner),
            new GitSourceCache(fileSystem),
            console,
            effectiveTransportPolicy
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

    public Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ResourceLimits? resourceLimits = null
    ) => BrowseAsync(projectDirectory, configuration, requestedPacks: null, resourceLimits);

    public Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseForUpdatesAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        IReadOnlyDictionary<string, string?> requestedPacks,
        ResourceLimits? resourceLimits = null,
        bool requireComplete = false
    ) =>
        BrowseAsync(
            projectDirectory,
            configuration,
            requestedPacks,
            resourceLimits,
            requireComplete
        );

    private async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        IReadOnlyDictionary<string, string?>? requestedPacks,
        ResourceLimits? resourceLimits = null,
        bool requireComplete = false
    )
    {
        var effectiveResourceLimits = resourceLimits ?? ResourceLimits.Default;
        _console.Debug($"Browsing {configuration.Sources.Count} configured pack sources");
        var ignoreContextResult = await _sourceIgnoreService.LoadAsync(projectDirectory);
        if (ignoreContextResult.Value is not { } ignoreContext)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                ignoreContextResult.Error ?? "Unable to load local source ignore settings."
            );
        }

        return await BrowseConfiguredSourcesAsync(
            projectDirectory,
            configuration,
            ignoreContext,
            requestedPacks,
            effectiveResourceLimits,
            requireComplete
        );
    }

    private async Task<
        ManifestOperationResult<IReadOnlyList<CatalogPack>>
    > BrowseConfiguredSourcesAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ConfiguredSourceIgnoreService.SourceIgnoreContext ignoreContext,
        IReadOnlyDictionary<string, string?>? requestedPacks,
        ResourceLimits resourceLimits,
        bool requireComplete
    )
    {
        var catalog = new List<CatalogPack>();
        var manifestBudget = new CatalogManifestBudget(resourceLimits);
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
            var sourceCatalog = await BrowseSourceAsync(
                projectDirectory,
                source,
                sourceOrder,
                requestedPacks,
                resourceLimits,
                requireComplete
            );
            if (sourceCatalog.Value is not { } sourcePacks)
            {
                var error = sourceCatalog.Error ?? "Source returned no catalog.";
                if (sourceCatalog.ErrorKind == ManifestOperationErrorKind.ResourceLimitExceeded)
                {
                    return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                        error,
                        sourceCatalog.ErrorKind
                    );
                }

                failedSources.Add($"{source.Name}: {error}");
                _console.Warning($"Skipping unavailable pack source '{source.Name}': {error}");
                continue;
            }

            successfulSourceCount++;
            var budget = manifestBudget.Observe(sourcePacks);
            if (!budget.IsSuccess)
            {
                return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    budget.Error!,
                    budget.ErrorKind
                );
            }

            catalog.AddRange(sourcePacks);
        }

        return CompleteConfiguredCatalog(
            catalog,
            configuration.Sources.Count,
            activeSourceCount,
            successfulSourceCount,
            failedSources,
            requireComplete
        );
    }

    private ManifestOperationResult<IReadOnlyList<CatalogPack>> CompleteConfiguredCatalog(
        List<CatalogPack> catalog,
        int configuredSourceCount,
        int activeSourceCount,
        int successfulSourceCount,
        List<string> failedSources,
        bool requireComplete
    )
    {
        if (activeSourceCount == 0 && configuredSourceCount > 0)
        {
            _console.Info("All configured pack sources are ignored locally.");
        }

        if (activeSourceCount > 0 && successfulSourceCount == 0)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                string.Join(Environment.NewLine, failedSources)
            );
        }

        if (requireComplete && failedSources.Count > 0)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                $"Unable to check all configured pack sources:{Environment.NewLine}{string.Join(Environment.NewLine, failedSources)}"
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
        ProjectConfiguration configuration,
        ResourceLimits? resourceLimits = null
    )
    {
        var effectiveResourceLimits = resourceLimits ?? ResourceLimits.Default;
        var ignoreContextResult = await _sourceIgnoreService.LoadAsync(projectDirectory);
        if (ignoreContextResult.Value is not { } ignoreContext)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                ignoreContextResult.Error ?? "Unable to load local source ignore settings."
            );
        }

        return await BrowseCachedSourcesAsync(
            projectDirectory,
            configuration,
            ignoreContext,
            effectiveResourceLimits
        );
    }

    private async Task<
        ManifestOperationResult<IReadOnlyList<CatalogPack>>
    > BrowseCachedSourcesAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ConfiguredSourceIgnoreService.SourceIgnoreContext ignoreContext,
        ResourceLimits resourceLimits
    )
    {
        var catalog = new List<CatalogPack>();
        var manifestBudget = new CatalogManifestBudget(resourceLimits);
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
                sourceOrder,
                resourceLimits
            );
            if (sourceCatalog.Value is { } sourcePacks)
            {
                var budget = manifestBudget.Observe(sourcePacks);
                if (!budget.IsSuccess)
                {
                    return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                        budget.Error!,
                        budget.ErrorKind
                    );
                }

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

                if (sourceCatalog.ErrorKind == ManifestOperationErrorKind.ResourceLimitExceeded)
                {
                    return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                        sourceCatalog.Error
                            ?? "A cached pack manifest exceeds its configured byte limit.",
                        sourceCatalog.ErrorKind
                    );
                }

                return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    sourceCatalog.Error ?? "Unable to browse cached pack sources."
                );
            }
        }

        return CompleteCachedCatalog(
            catalog,
            uncheckedOciSources,
            activeSourceCount,
            configuration.Sources.Count
        );
    }

    private ManifestOperationResult<IReadOnlyList<CatalogPack>> CompleteCachedCatalog(
        List<CatalogPack> catalog,
        List<string> uncheckedOciSources,
        int activeSourceCount,
        int configuredSourceCount
    )
    {
        if (activeSourceCount == 0 && configuredSourceCount > 0)
        {
            _console.Info("All configured pack sources are ignored locally.");
        }

        return catalog.Count == 0 && uncheckedOciSources.Count > 0
            ? ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                string.Join(Environment.NewLine, uncheckedOciSources)
            )
            : ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(catalog);
    }

    private Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseSourceAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        int sourceOrder,
        IReadOnlyDictionary<string, string?>? requestedPacks,
        ResourceLimits resourceLimits,
        bool requireComplete = false
    ) =>
        source switch
        {
            ProjectConfiguration.LocalSource localSource => _localPackDiscovery.BrowseAsync(
                _fileSystem.Path.GetFullPath(localSource.Path, projectDirectory),
                sourceOrder,
                localSource.Name,
                ConfiguredSourceIdentity.Create(localSource),
                resourceLimits
            ),
            ProjectConfiguration.GitSource gitSource => _gitPackDiscovery.BrowseAsync(
                projectDirectory,
                gitSource,
                sourceOrder,
                resourceLimits: resourceLimits
            ),
            ProjectConfiguration.OciSource ociSource => requestedPacks is null
                ? _ociPackSource.BrowseAsync(
                    projectDirectory,
                    ociSource,
                    sourceOrder,
                    resourceLimits: resourceLimits
                )
                : _ociPackSource.BrowseForUpdatesAsync(
                    projectDirectory,
                    ociSource,
                    sourceOrder,
                    requestedPacks,
                    resourceLimits: resourceLimits,
                    requireComplete: requireComplete
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
        int sourceOrder,
        ResourceLimits resourceLimits
    ) =>
        source switch
        {
            ProjectConfiguration.LocalSource localSource => _localPackDiscovery.BrowseAsync(
                _fileSystem.Path.GetFullPath(localSource.Path, projectDirectory),
                sourceOrder,
                localSource.Name,
                ConfiguredSourceIdentity.Create(localSource),
                resourceLimits
            ),
            ProjectConfiguration.GitSource gitSource => Task.FromResult(
                _gitPackDiscovery.BrowseCached(
                    projectDirectory,
                    gitSource,
                    sourceOrder,
                    resourceLimits
                )
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

    public Task<ManifestOperationResult<DiscoveredPack>> ResolveAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string packId,
        string? requestedVersion,
        ResourceLimits? resourceLimits = null,
        CatalogManifestBudget? workBudget = null
    ) =>
        ResolveFromConfiguredSourcesAsync(
            projectDirectory,
            configuration,
            packId,
            requestedVersion,
            ResolutionEligibility.AllCatalogReleases,
            resourceLimits ?? ResourceLimits.Default,
            workBudget
        );

    public Task<ManifestOperationResult<DiscoveredPack>> ResolveDirectlyInstallableAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string packId,
        string? requestedVersion,
        ResourceLimits? resourceLimits = null,
        CatalogManifestBudget? workBudget = null
    ) =>
        ResolveFromConfiguredSourcesAsync(
            projectDirectory,
            configuration,
            packId,
            requestedVersion,
            ResolutionEligibility.DirectlyInstallable,
            resourceLimits ?? ResourceLimits.Default,
            workBudget
        );

    private async Task<ManifestOperationResult<DiscoveredPack>> ResolveFromConfiguredSourcesAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string packId,
        string? requestedVersion,
        ResolutionEligibility eligibility,
        ResourceLimits resourceLimits,
        CatalogManifestBudget? workBudget
    )
    {
        var discovered = await DiscoverResolutionCandidatesAsync(
            projectDirectory,
            configuration,
            packId,
            requestedVersion,
            resourceLimits,
            workBudget
        );
        if (discovered.Value is not { } candidates)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                discovered.Error ?? "Unable to browse pack sources.",
                discovered.ErrorKind
            );
        }

        var selectionCandidates = eligibility switch
        {
            ResolutionEligibility.AllCatalogReleases => candidates.Packs,
            ResolutionEligibility.DirectlyInstallable => CatalogPackFilter.DirectInstallable(
                candidates.Packs
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(eligibility)),
        };
        var selected = SelectFromCatalog(selectionCandidates, packId, requestedVersion);
        if (selected.Value is not { } selectedPack)
        {
            if (candidates.Failures.Count > 0)
            {
                return ManifestOperationResult<DiscoveredPack>.Failure(
                    $"Resolution for pack '{packId}' is incomplete because one or more sources were unavailable: {string.Join("; ", candidates.Failures)}"
                );
            }

            if (
                eligibility == ResolutionEligibility.DirectlyInstallable
                && SelectFromCatalog(candidates.Packs, packId, requestedVersion).Value
                    is { } existingPack
                && !existingPack.Manifest.DirectInstallable
            )
            {
                return ManifestOperationResult<DiscoveredPack>.Failure(
                    $"Pack '{packId}' is not directly installable (directInstallable is false).",
                    ManifestOperationErrorKind.PackNotFound
                );
            }

            return ResolveFromCatalog(selectionCandidates, packId, requestedVersion);
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
                SourceSelection = GetSourceSelection(selectionCandidates, selectedPack),
            }
        );
    }

    public async Task<ManifestOperationResult<DiscoveredPack>> ResolveLockedOciAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ProjectLockFile.ResolvedPack lockedPack,
        ResourceLimits? resourceLimits = null,
        CatalogManifestBudget? workBudget = null,
        CancellationToken cancellationToken = default
    )
    {
        var effectiveResourceLimits = resourceLimits ?? ResourceLimits.Default;
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
                effectiveResourceLimits,
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

            var observed = (
                workBudget ?? new CatalogManifestBudget(effectiveResourceLimits)
            ).Observe(lockedRelease.ManifestByteCount);
            if (!observed.IsSuccess)
            {
                return ManifestOperationResult<DiscoveredPack>.Failure(
                    observed.Error!,
                    observed.ErrorKind
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
        ProjectLockFile.ResolvedPack lockedPack,
        ResourceLimits? resourceLimits = null,
        CatalogManifestBudget? workBudget = null
    )
    {
        var effectiveResourceLimits = resourceLimits ?? ResourceLimits.Default;
        if (lockedPack.OciSource is not null)
        {
            return await ResolveLockedOciAsync(
                projectDirectory,
                configuration,
                lockedPack,
                resourceLimits: effectiveResourceLimits,
                workBudget: workBudget
            );
        }

        return await ResolveLockedConfiguredSourceAsync(
            projectDirectory,
            configuration,
            lockedPack,
            effectiveResourceLimits,
            workBudget
        );
    }

    private async Task<ManifestOperationResult<DiscoveredPack>> ResolveLockedConfiguredSourceAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ProjectLockFile.ResolvedPack lockedPack,
        ResourceLimits resourceLimits,
        CatalogManifestBudget? workBudget
    )
    {
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
        var sourceCatalog = await BrowseLockedSourceAsync(
            projectDirectory,
            source,
            sourceOrder,
            lockedPack,
            resourceLimits
        );

        if (sourceCatalog.Value is not { } candidates)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                $"Locked pack '{lockedPack.Id}@{lockedPack.Version}' cannot be read from source '{source.Name}': {sourceCatalog.Error ?? "source unavailable"}.",
                sourceCatalog.ErrorKind
            );
        }

        var observed = (workBudget ?? new CatalogManifestBudget(resourceLimits)).Observe(
            candidates
        );
        if (!observed.IsSuccess)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                observed.Error!,
                observed.ErrorKind
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

    private async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseLockedSourceAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        int sourceOrder,
        ProjectLockFile.ResolvedPack lockedPack,
        ResourceLimits resourceLimits
    )
    {
        if (
            source is not ProjectConfiguration.GitSource gitSource
            || lockedPack.GitSource is not { } gitProvenance
        )
        {
            return await BrowseSourceAsync(
                projectDirectory,
                source,
                sourceOrder,
                requestedPacks: null,
                resourceLimits
            );
        }

        if (
            !string.Equals(gitSource.Url, gitProvenance.Url, StringComparison.Ordinal)
            || !string.Equals(gitSource.Ref, gitProvenance.Ref, StringComparison.Ordinal)
            || !string.Equals(gitSource.Path, gitProvenance.Path, StringComparison.Ordinal)
        )
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                $"Locked pack '{lockedPack.Id}@{lockedPack.Version}' Git provenance does not match its configured source.",
                ManifestOperationErrorKind.SourceIntegrityMismatch
            );
        }

        return await _gitPackDiscovery.BrowseCommitAsync(
            gitSource,
            sourceOrder,
            gitProvenance.ResolvedCommit,
            resourceLimits
        );
    }

    public async Task<ManifestOperationResult<CatalogPack?>> ResolveLockedOciTagAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ProjectLockFile.ResolvedPack lockedPack,
        ResourceLimits? resourceLimits = null,
        CatalogManifestBudget? workBudget = null,
        CancellationToken cancellationToken = default
    )
    {
        var effectiveResourceLimits = resourceLimits ?? ResourceLimits.Default;
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
            effectiveResourceLimits,
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

        var observed = (workBudget ?? new CatalogManifestBudget(effectiveResourceLimits)).Observe(
            resolvedCandidates
        );
        if (!observed.IsSuccess)
        {
            return ManifestOperationResult<CatalogPack?>.Failure(
                observed.Error!,
                observed.ErrorKind
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
        string? requestedVersion,
        ResourceLimits resourceLimits,
        CatalogManifestBudget? workBudget = null
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
        var manifestBudget = workBudget ?? new CatalogManifestBudget(resourceLimits);
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

            var sourcePacks = await DiscoverSourceResolutionCandidatesAsync(
                projectDirectory,
                source,
                sourceOrder,
                packId,
                requestedVersion,
                resourceLimits
            );
            if (sourcePacks.Value is { } candidates)
            {
                var budget = manifestBudget.Observe(candidates);
                if (!budget.IsSuccess)
                {
                    return ManifestOperationResult<ResolutionCandidates>.Failure(
                        budget.Error!,
                        budget.ErrorKind
                    );
                }

                packs.AddRange(candidates);
            }
            else
            {
                var error = sourcePacks.Error ?? "Source returned no resolution candidates.";
                if (sourcePacks.ErrorKind == ManifestOperationErrorKind.ResourceLimitExceeded)
                {
                    return ManifestOperationResult<ResolutionCandidates>.Failure(
                        error,
                        sourcePacks.ErrorKind
                    );
                }

                failures.Add($"{source.Name}: {error}");
                _console.Warning($"Skipping unavailable pack source '{source.Name}': {error}");
            }
        }

        return ManifestOperationResult<ResolutionCandidates>.Success(
            new ResolutionCandidates(packs, failures)
        );
    }

    private async Task<
        ManifestOperationResult<IReadOnlyList<CatalogPack>>
    > DiscoverSourceResolutionCandidatesAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        int sourceOrder,
        string packId,
        string? requestedVersion,
        ResourceLimits resourceLimits
    )
    {
        return source switch
        {
            ProjectConfiguration.LocalSource localSource => await _localPackDiscovery.BrowseAsync(
                _fileSystem.Path.GetFullPath(localSource.Path, projectDirectory),
                sourceOrder,
                localSource.Name,
                ConfiguredSourceIdentity.Create(localSource),
                resourceLimits
            ),
            ProjectConfiguration.GitSource gitSource => await _gitPackDiscovery.BrowseAsync(
                projectDirectory,
                gitSource,
                sourceOrder,
                resourceLimits: resourceLimits
            ),
            ProjectConfiguration.OciSource ociSource => await _ociPackSource.ResolveCandidatesAsync(
                projectDirectory,
                ociSource,
                sourceOrder,
                packId,
                requestedVersion,
                resourceLimits: resourceLimits
            ),
            _ => ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                "Pack source type is unsupported."
            ),
        };
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
