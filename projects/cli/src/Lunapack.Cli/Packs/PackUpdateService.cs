namespace Lunapack.Cli;

internal sealed class PackUpdateService(
    PackCatalog packCatalog,
    PackLifecycleService packLifecycleService,
    ProjectStateStore projectStateStore,
    ISourceSwitchConfirmer? configuredSourceSwitchConfirmer = null
)
{
    private readonly ISourceSwitchConfirmer _sourceSwitchConfirmer =
        configuredSourceSwitchConfirmer ?? new DenySourceSwitchConfirmer();

    public async Task<UpdateResult> UpdateAsync(
        string projectDirectory,
        PackReference? packReference,
        bool dryRun = false,
        ScriptExecutionMode? scriptMode = null
    ) =>
        await this.UpdateAsync(
            projectDirectory,
            packReference,
            selectedUpdateIds: null,
            dryRun,
            scriptMode ?? ScriptExecutionMode.Prompt
        );

    public async Task<UpdateResult> UpdateSelectedAsync(
        string projectDirectory,
        IReadOnlySet<string> selectedUpdateIds,
        bool dryRun = false,
        ScriptExecutionMode? scriptMode = null
    ) =>
        await this.UpdateAsync(
            projectDirectory,
            packReference: null,
            selectedUpdateIds,
            dryRun,
            scriptMode ?? ScriptExecutionMode.Prompt
        );

    private async Task<UpdateResult> UpdateAsync(
        string projectDirectory,
        PackReference? packReference,
        IReadOnlySet<string>? selectedUpdateIds,
        bool dryRun,
        ScriptExecutionMode scriptMode
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return UpdateResult.Failure(loadedState.Error ?? "Unable to load project state.");
        }

        if (state.Configuration.Sources.Count == 0)
        {
            return UpdateResult.Failure("No sources are configured.");
        }

        var catalog = await packCatalog.BrowseAsync(projectDirectory, state.Configuration);
        if (catalog.Value is not { } catalogPacks)
        {
            return UpdateResult.Failure(catalog.Error ?? "Unable to browse pack sources.");
        }

        return packReference is { } reference
            ? await UpdateNamedAsync(
                projectDirectory,
                state,
                catalogPacks,
                reference,
                dryRun,
                scriptMode
            )
            : await UpdateAllAsync(
                projectDirectory,
                state,
                catalogPacks,
                selectedUpdateIds,
                dryRun,
                scriptMode
            );
    }

    private async Task<UpdateResult> UpdateNamedAsync(
        string projectDirectory,
        ProjectState state,
        IReadOnlyList<CatalogPack> catalog,
        PackReference packReference,
        bool dryRun,
        ScriptExecutionMode scriptMode
    )
    {
        var selectedUpdate = SelectNamedUpdate(state, catalog, packReference);
        if (selectedUpdate.Value is not { } update)
        {
            return UpdateResult.Failure(
                selectedUpdate.Error ?? $"Pack '{packReference.Id}' is unavailable."
            );
        }

        if (update.IsCurrent)
        {
            return UpdateResult.Success(
                [
                    new UpdateOutcome(
                        update.RequestedRoot.Id,
                        update.CurrentPack.Version,
                        update.SelectedPack.Manifest.Version,
                        IsCurrent: true
                    ),
                ],
                dryRun ? new PackUpdatePlan([]) : null
            );
        }

        if (
            !dryRun
            && update.SourceSwitch is { } sourceSwitch
            && !_sourceSwitchConfirmer.Confirm(sourceSwitch)
        )
        {
            return UpdateResult.Failure(
                $"Source switch for pack '{sourceSwitch.PackId}' was not confirmed."
            );
        }

        var nextRequestedRoots = state
            .Configuration.Packs.Select(root =>
                string.Equals(root.Id, packReference.Id, StringComparison.Ordinal)
                    ? update.NextRequestedRoot
                    : root
            )
            .ToList();
        var outcome = new UpdateOutcome(
            update.RequestedRoot.Id,
            update.CurrentPack.Version,
            update.SelectedPack.Manifest.Version,
            IsCurrent: false
        );
        return await ApplyAsync(
            projectDirectory,
            nextRequestedRoots,
            update.NextRequestedRoot,
            [outcome],
            dryRun,
            scriptMode,
            update.SourceSwitch
        );
    }

    private static ManifestOperationResult<NamedPackUpdate> SelectNamedUpdate(
        ProjectState state,
        IReadOnlyList<CatalogPack> catalog,
        PackReference packReference
    )
    {
        var requestedRoot = state.Configuration.Packs.Find(request =>
            string.Equals(request.Id, packReference.Id, StringComparison.Ordinal)
        );
        if (requestedRoot is null)
        {
            return ManifestOperationResult<NamedPackUpdate>.Failure(
                $"Pack '{packReference.Id}' is not installed."
            );
        }

        var currentPack = state.LockFile.Packs.Find(pack =>
            string.Equals(pack.Id, packReference.Id, StringComparison.Ordinal)
        );
        if (currentPack is null)
        {
            return ManifestOperationResult<NamedPackUpdate>.Failure(
                $"Lock file does not contain requested pack '{packReference.Id}'."
            );
        }

        var selected = packReference.Version is null
            ? SelectOrdinaryUpdate(currentPack, catalog)
            : LockedSourceUpdateSelector.SelectExplicit(
                currentPack,
                catalog,
                packReference.Version
            );
        if (selected.Value is not { } selection)
        {
            return ManifestOperationResult<NamedPackUpdate>.Failure(
                selected.Error ?? $"Pack '{packReference.Id}' is unavailable."
            );
        }

        var isCurrent = PackUpdateSelectionService.IsCurrent(currentPack, selection.Candidate);
        if (!isCurrent.IsSuccess)
        {
            return ManifestOperationResult<NamedPackUpdate>.Failure(
                isCurrent.Error ?? "Unable to compare pack versions."
            );
        }

        var nextRequestedRoot = requestedRoot with
        {
            Version = packReference.Version is null ? null : selection.Candidate.Manifest.Version,
        };
        return ManifestOperationResult<NamedPackUpdate>.Success(
            new NamedPackUpdate(
                requestedRoot,
                currentPack,
                selection.Candidate,
                nextRequestedRoot,
                isCurrent.Value,
                selection.SourceSwitch
            )
        );
    }

    private static ManifestOperationResult<LockedSourceUpdateSelector.ExplicitSelection> SelectOrdinaryUpdate(
        ProjectLockFile.ResolvedPack currentPack,
        IReadOnlyList<CatalogPack> catalog
    )
    {
        var selectedPack = LockedSourceUpdateSelector.SelectOrdinary(currentPack, catalog);
        return selectedPack is { } candidate
            ? ManifestOperationResult<LockedSourceUpdateSelector.ExplicitSelection>.Success(
                new LockedSourceUpdateSelector.ExplicitSelection(candidate, null)
            )
            : ManifestOperationResult<LockedSourceUpdateSelector.ExplicitSelection>.Failure(
                $"Pack '{currentPack.Id}' is unavailable from its locked source."
            );
    }

    private async Task<UpdateResult> UpdateAllAsync(
        string projectDirectory,
        ProjectState state,
        IReadOnlyList<CatalogPack> catalog,
        IReadOnlySet<string>? selectedUpdateIds,
        bool dryRun,
        ScriptExecutionMode scriptMode
    )
    {
        var selectedUpdates = PackUpdateSelectionService.SelectAvailable(state, catalog);
        if (selectedUpdates.Value is not { } availableUpdates)
        {
            return UpdateResult.Failure(
                selectedUpdates.Error ?? "Unable to select available updates."
            );
        }

        var updates = availableUpdates.ToList();
        if (selectedUpdateIds is not null)
        {
            updates.RemoveAll(update => !selectedUpdateIds.Contains(update.RequestedRoot.Id));
        }

        if (updates.Count == 0)
        {
            return UpdateResult.Success([], dryRun ? new PackUpdatePlan([]) : null);
        }

        var selectedIds = updates
            .Select(update => update.RequestedRoot.Id)
            .ToHashSet(StringComparer.Ordinal);
        var nextRequestedRoots = state
            .Configuration.Packs.Select(root =>
                selectedIds.Contains(root.Id) ? root with { Version = null } : root
            )
            .ToList();
        var outcomes = updates
            .Select(update => new UpdateOutcome(
                update.RequestedRoot.Id,
                update.Current.Version,
                update.Latest.Manifest.Version,
                IsCurrent: false
            ))
            .ToList();
        return await ApplyAsync(
            projectDirectory,
            nextRequestedRoots,
            nextRequestedRoots[0],
            outcomes,
            dryRun,
            scriptMode
        );
    }

    private async Task<UpdateResult> ApplyAsync(
        string projectDirectory,
        IReadOnlyList<ProjectConfiguration.RequestedPack> selectedRequestedRoots,
        ProjectConfiguration.RequestedPack updateRequestRoot,
        IReadOnlyList<UpdateOutcome> outcomes,
        bool dryRun,
        ScriptExecutionMode scriptMode,
        LockedSourceUpdateSelector.SourceSwitch? proposedSourceSwitch = null
    )
    {
        var updateRequest = new PackInstallationRequest(
            new PackReference(updateRequestRoot.Id, updateRequestRoot.Version),
            updateRequestRoot.Destination,
            false
        )
        {
            ScriptMode = scriptMode,
        };
        if (dryRun)
        {
            var plannedUpdate = await packLifecycleService.DryRunUpdateAsync(
                projectDirectory,
                selectedRequestedRoots,
                updateRequest
            );
            return plannedUpdate.Value is { } updatePlan
                ? UpdateResult.Success(outcomes, updatePlan, proposedSourceSwitch)
                : UpdateResult.Failure(plannedUpdate.Error ?? "Unable to plan pack update.");
        }

        var exitCode = await packLifecycleService.UpdateAsync(
            projectDirectory,
            selectedRequestedRoots,
            updateRequest
        );
        return exitCode == 0 ? UpdateResult.Success(outcomes) : UpdateResult.LifecycleFailure();
    }

    internal sealed record UpdateResult(
        IReadOnlyList<UpdateOutcome> Outcomes,
        string? Error,
        bool IsLifecycleFailure,
        PackUpdatePlan? DryRunPlan,
        LockedSourceUpdateSelector.SourceSwitch? ProposedSourceSwitch
    )
    {
        public static UpdateResult Failure(string error) => new([], error, false, null, null);

        public static UpdateResult LifecycleFailure() => new([], null, true, null, null);

        public static UpdateResult Success(
            IReadOnlyList<UpdateOutcome> outcomes,
            PackUpdatePlan? dryRunPlan = null,
            LockedSourceUpdateSelector.SourceSwitch? proposedSourceSwitch = null
        ) => new(outcomes, null, false, dryRunPlan, proposedSourceSwitch);
    }

    internal sealed record UpdateOutcome(
        string Id,
        string CurrentVersion,
        string SelectedVersion,
        bool IsCurrent
    );

    private sealed record NamedPackUpdate(
        ProjectConfiguration.RequestedPack RequestedRoot,
        ProjectLockFile.ResolvedPack CurrentPack,
        CatalogPack SelectedPack,
        ProjectConfiguration.RequestedPack NextRequestedRoot,
        bool IsCurrent,
        LockedSourceUpdateSelector.SourceSwitch? SourceSwitch
    );
}
