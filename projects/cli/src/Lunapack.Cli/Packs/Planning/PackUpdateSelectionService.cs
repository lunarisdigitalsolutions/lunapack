using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using NuGet.Versioning;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class PackUpdateSelectionService(
    PackCatalog packCatalog,
    ProjectStateStore projectStateStore,
    PackLifecycleService packLifecycleService,
    ConfiguredSourceIgnoreService? configuredSourceIgnoreService = null
)
{
    private readonly ConfiguredSourceIgnoreService? _sourceIgnoreService =
        configuredSourceIgnoreService;

    public async Task<ManifestOperationResult<PackUpdateAvailability>> GetAvailableAsync(
        string projectDirectory,
        bool offline = false
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<PackUpdateAvailability>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        if (state.Configuration.Sources.Count == 0)
        {
            return ManifestOperationResult<PackUpdateAvailability>.Failure(
                "No sources are configured."
            );
        }

        if (offline)
        {
            return ManifestOperationResult<PackUpdateAvailability>.Success(
                CreateOfflineAvailability(state)
            );
        }

        var ignoredRootError = await FindIgnoredRootPackErrorAsync(projectDirectory, state);
        if (!ignoredRootError.IsSuccess)
        {
            return ManifestOperationResult<PackUpdateAvailability>.Failure(
                ignoredRootError.Error ?? "Unable to check local source ignore settings."
            );
        }

        if (ignoredRootError.Value is { } ignoredError)
        {
            return ManifestOperationResult<PackUpdateAvailability>.Failure(ignoredError);
        }

        var catalog = await packCatalog.BrowseAsync(projectDirectory, state.Configuration);
        if (catalog.Value is not { } catalogPacks)
        {
            return ManifestOperationResult<PackUpdateAvailability>.Failure(
                catalog.Error ?? "Unable to browse pack sources."
            );
        }

        var integrity = await CheckLockedOciReleasesAsync(projectDirectory, state);
        if (integrity.Value is not { } integrityViolations)
        {
            return ManifestOperationResult<PackUpdateAvailability>.Failure(
                integrity.Error ?? "Unable to verify locked OCI releases."
            );
        }

        var selected = SelectAvailable(state, catalogPacks);
        if (selected.Value is not { } versionUpdates)
        {
            return ManifestOperationResult<PackUpdateAvailability>.Failure(
                selected.Error ?? "Unable to select available updates."
            );
        }

        var updates = await AddExternalContentUpdatesAsync(
            projectDirectory,
            state,
            catalogPacks,
            versionUpdates
        );
        return updates.Value is { } availableUpdates
            ? ManifestOperationResult<PackUpdateAvailability>.Success(
                new(availableUpdates, integrityViolations, [])
            )
            : ManifestOperationResult<PackUpdateAvailability>.Failure(
                updates.Error ?? "Unable to inspect external source content."
            );
    }

    private static PackUpdateAvailability CreateOfflineAvailability(ProjectState state) =>
        new(
            [],
            [],
            [
                .. state
                    .LockFile.Packs.Where(pack => pack.OciSource is not null)
                    .OrderBy(pack => pack.Id, StringComparer.Ordinal),
            ]
        );

    private async Task<ManifestOperationResult<string?>> FindIgnoredRootPackErrorAsync(
        string projectDirectory,
        ProjectState state
    )
    {
        if (_sourceIgnoreService is null)
        {
            return ManifestOperationResult<string?>.Success(null);
        }

        var rootIds = state
            .Configuration.Packs.Select(pack => pack.Id)
            .ToHashSet(StringComparer.Ordinal);
        return await _sourceIgnoreService.FindIgnoredLockedPackErrorAsync(
            projectDirectory,
            state.Configuration,
            state.LockFile.Packs.Where(pack => rootIds.Contains(pack.Id))
        );
    }

    private async Task<
        ManifestOperationResult<IReadOnlyList<OciReleaseIntegrityViolation>>
    > CheckLockedOciReleasesAsync(string projectDirectory, ProjectState state)
    {
        var violations = new List<OciReleaseIntegrityViolation>();
        foreach (var lockedPack in state.LockFile.Packs)
        {
            if (lockedPack.OciSource is null)
            {
                continue;
            }

            var observed = await packCatalog.ResolveLockedOciTagAsync(
                projectDirectory,
                state.Configuration,
                lockedPack
            );
            if (!observed.IsSuccess)
            {
                return ManifestOperationResult<IReadOnlyList<OciReleaseIntegrityViolation>>.Failure(
                    observed.Error
                        ?? $"Unable to verify locked OCI release '{lockedPack.Id}@{lockedPack.Version}'.",
                    observed.ErrorKind
                );
            }

            var violation = CompareLockedOciRelease(lockedPack, observed.Value);
            if (violation is not null)
            {
                violations.Add(violation);
            }
        }

        return ManifestOperationResult<IReadOnlyList<OciReleaseIntegrityViolation>>.Success(
            violations
        );
    }

    internal static OciReleaseIntegrityViolation? CompareLockedOciRelease(
        ProjectLockFile.ResolvedPack lockedPack,
        CatalogPack? observedPack
    )
    {
        if (lockedPack.OciSource is not { } lockedProvenance)
        {
            return null;
        }

        if (observedPack?.OciSource is not { } observedProvenance)
        {
            return new OciReleaseIntegrityViolation(lockedPack, observedPack);
        }

        return
            string.Equals(
                lockedProvenance.ManifestDigest,
                observedProvenance.ManifestDigest,
                StringComparison.Ordinal
            )
            && string.Equals(
                lockedProvenance.ContentDigest,
                observedProvenance.ContentDigest,
                StringComparison.Ordinal
            )
            && lockedProvenance.ContentSize == observedProvenance.ContentSize
            ? null
            : new OciReleaseIntegrityViolation(lockedPack, observedPack);
    }

    private async Task<
        ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>
    > AddExternalContentUpdatesAsync(
        string projectDirectory,
        ProjectState state,
        IReadOnlyList<CatalogPack> catalog,
        IReadOnlyList<AvailablePackUpdate> versionUpdates
    )
    {
        var updates = versionUpdates.ToList();
        var updatedIds = updates
            .Select(update => update.RequestedRoot.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var root in state.Configuration.Packs)
        {
            var current = state.LockFile.Packs.Find(pack =>
                string.Equals(pack.Id, root.Id, StringComparison.Ordinal)
            );
            if (
                current is null
                || current.ExternalSources.Count == 0
                || updatedIds.Contains(root.Id)
            )
            {
                continue;
            }

            var selectedPack = LockedSourceUpdateSelector.SelectOrdinary(current, catalog);
            if (selectedPack is null)
            {
                continue;
            }

            var preview = await packLifecycleService.DryRunUpdateAsync(
                projectDirectory,
                state.Configuration.Packs,
                new PackInstallationRequest(
                    new PackReference(root.Id, root.Version),
                    root.Destination,
                    false
                )
            );
            if (preview.Value is not { } plan)
            {
                var reason = GetExternalSourceFailureReason(preview.Error);
                if (reason is null)
                {
                    return ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>.Failure(
                        preview.Error ?? "Unable to inspect external source content."
                    );
                }

                updates.Add(new AvailablePackUpdate(root, current, selectedPack, reason));
                continue;
            }

            if (plan.Actions.Count > 0)
            {
                updates.Add(
                    new AvailablePackUpdate(root, current, selectedPack, "external source changed")
                );
            }
        }

        return ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>.Success(updates);
    }

    private static string? GetExternalSourceFailureReason(string? error) =>
        error?.Contains("missing configured source", StringComparison.OrdinalIgnoreCase) is true
            ? "external source missing"
        : error?.Contains("drift", StringComparison.OrdinalIgnoreCase) is true
            ? "external source drift"
        : null;

    internal static ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>> SelectAvailable(
        ProjectState state,
        IReadOnlyList<CatalogPack> catalog
    )
    {
        var currentPacks = state.LockFile.Packs.ToDictionary(
            pack => pack.Id,
            StringComparer.Ordinal
        );
        var updates = new List<AvailablePackUpdate>();
        var requestedRoots = state.Configuration.Packs.OrderBy(
            pack => pack.Id,
            StringComparer.Ordinal
        );
        foreach (var requestedRoot in requestedRoots)
        {
            if (!currentPacks.TryGetValue(requestedRoot.Id, out var currentPack))
            {
                return ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>.Failure(
                    $"Lock file does not contain requested pack '{requestedRoot.Id}'."
                );
            }

            if (!NuGetVersion.TryParse(currentPack.Version, out var currentVersion))
            {
                return ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>.Failure(
                    $"Lock file version '{currentPack.Version}' for '{currentPack.Id}' is invalid."
                );
            }

            var latestPack = LockedSourceUpdateSelector.SelectOrdinary(currentPack, catalog);
            if (
                latestPack is not null
                && VersionComparer.VersionRelease.Compare(latestPack.Version, currentVersion) > 0
            )
            {
                updates.Add(new AvailablePackUpdate(requestedRoot, currentPack, latestPack));
            }
        }

        return ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>.Success(updates);
    }

    internal static ManifestOperationResult<bool> IsCurrent(
        ProjectLockFile.ResolvedPack currentPack,
        CatalogPack selectedPack
    )
    {
        if (!NuGetVersion.TryParse(currentPack.Version, out var currentVersion))
        {
            return ManifestOperationResult<bool>.Failure(
                $"Lock file version '{currentPack.Version}' for '{currentPack.Id}' is invalid."
            );
        }

        return ManifestOperationResult<bool>.Success(
            VersionComparer.VersionRelease.Compare(selectedPack.Version, currentVersion) == 0
        );
    }
}
