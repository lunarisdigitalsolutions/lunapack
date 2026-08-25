using NuGet.Versioning;

namespace Lunapack.Cli;

internal sealed class PackUpdateSelectionService(
    PackCatalog packCatalog,
    ProjectStateStore projectStateStore
)
{
    public async Task<
        ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>
    > GetAvailableAsync(string projectDirectory)
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

        var catalog = await packCatalog.BrowseAsync(projectDirectory, state.Configuration);
        if (catalog.Value is not { } catalogPacks)
        {
            return ManifestOperationResult<IReadOnlyList<AvailablePackUpdate>>.Failure(
                catalog.Error ?? "Unable to browse pack sources."
            );
        }

        return SelectAvailable(state, catalogPacks);
    }

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
