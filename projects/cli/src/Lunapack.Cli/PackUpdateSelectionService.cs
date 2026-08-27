using NuGet.Versioning;

namespace Lunapack.Cli;

internal sealed class PackUpdateSelectionService(
    PackCatalog packCatalog,
    ProjectStateStore projectStateStore,
    PackLifecycleService packLifecycleService
)
{
    public async Task<
        ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>
    > GetAvailableAsync(string projectDirectory, bool offline = false)
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        if (state.Configuration.Sources.Count == 0)
        {
            return ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>.Failure(
                "No sources are configured."
            );
        }

        if (offline)
        {
            return ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>.Success([]);
        }

        var catalog = await packCatalog.BrowseAsync(projectDirectory, state.Configuration);
        if (catalog.Value is not { } catalogPacks)
        {
            return ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>.Failure(
                catalog.Error ?? "Unable to browse pack sources."
            );
        }

        var selected = SelectAvailable(state, catalogPacks);
        if (selected.Value is not { } versionUpdates)
        {
            return selected;
        }

        return await AddExternalContentUpdatesAsync(
            projectDirectory,
            state,
            catalogPacks,
            versionUpdates
        );
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
        foreach (
            var requestedRoot in state.Configuration.Packs.OrderBy(
                pack => pack.Id,
                StringComparer.Ordinal
            )
        )
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
