using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs;

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
        ScriptExecutionMode? scriptMode = null,
        bool skipInstructions = false,
        bool acceptSources = false
    ) =>
        await UpdateAsync(
            projectDirectory,
            packReference,
            selectedUpdateIds: null,
            dryRun,
            scriptMode ?? ScriptExecutionMode.Prompt,
            skipInstructions,
            acceptSources
        );

    public async Task<UpdateResult> UpdateSelectedAsync(
        string projectDirectory,
        IReadOnlySet<string> selectedUpdateIds,
        bool dryRun = false,
        ScriptExecutionMode? scriptMode = null,
        bool skipInstructions = false,
        bool acceptSources = false
    ) =>
        await UpdateAsync(
            projectDirectory,
            packReference: null,
            selectedUpdateIds,
            dryRun,
            scriptMode ?? ScriptExecutionMode.Prompt,
            skipInstructions,
            acceptSources
        );

    private async Task<UpdateResult> UpdateAsync(
        string projectDirectory,
        PackReference? packReference,
        IReadOnlySet<string>? selectedUpdateIds,
        bool dryRun,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources
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
                scriptMode,
                skipInstructions,
                acceptSources
            )
            : await UpdateAllAsync(
                projectDirectory,
                state,
                catalogPacks,
                selectedUpdateIds,
                dryRun,
                scriptMode,
                skipInstructions,
                acceptSources
            );
    }

    private async Task<UpdateResult> UpdateNamedAsync(
        string projectDirectory,
        ProjectState state,
        IReadOnlyList<CatalogPack> catalog,
        PackReference packReference,
        bool dryRun,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources
    )
    {
        var selectedUpdate = SelectNamedUpdate(state, catalog, packReference);
        if (selectedUpdate.Value is not { } update)
        {
            return UpdateResult.Failure(
                selectedUpdate.Error ?? $"Pack '{packReference.Id}' is unavailable."
            );
        }

        var terminalResult = GetTerminalNamedUpdateResult(update, dryRun);
        if (terminalResult is not null)
        {
            return terminalResult;
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
        if (update.IsCurrent)
        {
            var previewResult = await PreviewCurrentExternalUpdateAsync(
                projectDirectory,
                nextRequestedRoots,
                update.NextRequestedRoot,
                outcome,
                dryRun,
                scriptMode,
                skipInstructions,
                acceptSources
            );
            if (previewResult is not null)
            {
                return previewResult;
            }
        }

        return await ApplyAsync(
            projectDirectory,
            nextRequestedRoots,
            update.NextRequestedRoot,
            [outcome],
            dryRun,
            scriptMode,
            skipInstructions,
            acceptSources,
            update.SourceSwitch
        );
    }

    private UpdateResult? GetTerminalNamedUpdateResult(NamedPackUpdate update, bool dryRun)
    {
        if (update.IsCurrent && update.CurrentPack.ExternalSources.Count == 0)
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

        if (dryRun || update.SourceSwitch is not { } sourceSwitch)
        {
            return null;
        }

        var sourceSwitchConfirmed = _sourceSwitchConfirmer.Confirm(sourceSwitch);
        if (!sourceSwitchConfirmed)
        {
            return UpdateResult.Failure(
                $"Source switch for pack '{sourceSwitch.PackId}' was not confirmed."
            );
        }

        return null;
    }

    private async Task<UpdateResult?> PreviewCurrentExternalUpdateAsync(
        string projectDirectory,
        IReadOnlyList<ProjectConfiguration.RequestedPack> nextRequestedRoots,
        ProjectConfiguration.RequestedPack nextRequestedRoot,
        UpdateOutcome outcome,
        bool dryRun,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources
    )
    {
        var preview = await ApplyAsync(
            projectDirectory,
            nextRequestedRoots,
            nextRequestedRoot,
            [outcome],
            dryRun: true,
            scriptMode,
            skipInstructions,
            acceptSources
        );
        if (preview.Error is not null || preview.IsLifecycleFailure)
        {
            return preview;
        }

        if (preview.FileChangePlan?.Actions.Count == 0)
        {
            return UpdateResult.Success(
                [outcome with { IsCurrent = true }],
                dryRun ? preview.FileChangePlan : null
            );
        }

        return dryRun ? preview : null;
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
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources
    )
    {
        var selectedUpdates = SelectVersionUpdates(state, catalog, selectedUpdateIds);
        if (selectedUpdates.Value is not { } updates)
        {
            return UpdateResult.Failure(selectedUpdates.Error ?? "Unable to select updates.");
        }

        var versionUpdateIds = updates
            .Select(update => update.RequestedRoot.Id)
            .ToHashSet(StringComparer.Ordinal);
        var currentPacks = state.LockFile.Packs.ToDictionary(
            pack => pack.Id,
            StringComparer.Ordinal
        );
        var externalRefreshRoots = SelectExternalRefreshRoots(
            state,
            selectedUpdateIds,
            versionUpdateIds,
            currentPacks
        );
        if (updates.Count == 0 && externalRefreshRoots.Count == 0)
        {
            return UpdateResult.Success([], dryRun ? new PackUpdatePlan([]) : null);
        }

        var nextRequestedRoots = state
            .Configuration.Packs.Select(root =>
                versionUpdateIds.Contains(root.Id) ? root with { Version = null } : root
            )
            .ToList();
        var outcomes = CreateUpdateOutcomes(updates);
        if (externalRefreshRoots.Count > 0)
        {
            var previewResult = await PreviewExternalRefreshAsync(
                projectDirectory,
                nextRequestedRoots,
                externalRefreshRoots,
                currentPacks,
                updates.Count,
                outcomes,
                dryRun,
                scriptMode,
                skipInstructions,
                acceptSources
            );
            if (previewResult is not null)
            {
                return previewResult;
            }
        }

        return await ApplyAsync(
            projectDirectory,
            nextRequestedRoots,
            nextRequestedRoots[0],
            outcomes,
            dryRun,
            scriptMode,
            skipInstructions,
            acceptSources
        );
    }

    private static ManifestOperationResult<List<AvailablePackUpdate>> SelectVersionUpdates(
        ProjectState state,
        IReadOnlyList<CatalogPack> catalog,
        IReadOnlySet<string>? selectedUpdateIds
    )
    {
        var selected = PackUpdateSelectionService.SelectAvailable(state, catalog);
        if (selected.Value is not { } availableUpdates)
        {
            return ManifestOperationResult<List<AvailablePackUpdate>>.Failure(
                selected.Error ?? "Unable to select available updates."
            );
        }

        var updates = availableUpdates.ToList();
        if (selectedUpdateIds is not null)
        {
            updates.RemoveAll(update => !selectedUpdateIds.Contains(update.RequestedRoot.Id));
        }

        return ManifestOperationResult<List<AvailablePackUpdate>>.Success(updates);
    }

    private static List<UpdateOutcome> CreateUpdateOutcomes(
        IEnumerable<AvailablePackUpdate> updates
    ) =>
        [
            .. updates.Select(update => new UpdateOutcome(
                update.RequestedRoot.Id,
                update.Current.Version,
                update.Latest.Manifest.Version,
                IsCurrent: false
            )),
        ];

    private static List<ProjectConfiguration.RequestedPack> SelectExternalRefreshRoots(
        ProjectState state,
        IReadOnlySet<string>? selectedUpdateIds,
        HashSet<string> versionUpdateIds,
        Dictionary<string, ProjectLockFile.ResolvedPack> currentPacks
    ) =>
        [
            .. state.Configuration.Packs.Where(root =>
                !versionUpdateIds.Contains(root.Id)
                && (selectedUpdateIds is null || selectedUpdateIds.Contains(root.Id))
                && currentPacks.TryGetValue(root.Id, out var current)
                && current.ExternalSources.Count > 0
            ),
        ];

    private async Task<UpdateResult?> PreviewExternalRefreshAsync(
        string projectDirectory,
        IReadOnlyList<ProjectConfiguration.RequestedPack> nextRequestedRoots,
        List<ProjectConfiguration.RequestedPack> externalRefreshRoots,
        Dictionary<string, ProjectLockFile.ResolvedPack> currentPacks,
        int versionUpdateCount,
        List<UpdateOutcome> outcomes,
        bool dryRun,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources
    )
    {
        var externalOutcomes = externalRefreshRoots
            .Select(root => currentPacks[root.Id])
            .Select(pack => new UpdateOutcome(
                pack.Id,
                pack.Version,
                pack.Version,
                IsCurrent: false
            ))
            .ToList();
        var preview = await ApplyAsync(
            projectDirectory,
            nextRequestedRoots,
            externalRefreshRoots[0],
            [.. outcomes, .. externalOutcomes],
            dryRun: true,
            scriptMode,
            skipInstructions,
            acceptSources
        );
        if (preview.Error is not null || preview.IsLifecycleFailure)
        {
            return preview;
        }

        if (versionUpdateCount == 0 && preview.FileChangePlan?.Actions.Count == 0)
        {
            return UpdateResult.Success([], dryRun ? preview.FileChangePlan : null);
        }

        if (versionUpdateCount == 0)
        {
            outcomes.AddRange(externalOutcomes);
        }

        return dryRun
            ? UpdateResult.Success(outcomes, preview.FileChangePlan ?? new PackUpdatePlan([]))
            : null;
    }

    private async Task<UpdateResult> ApplyAsync(
        string projectDirectory,
        IReadOnlyList<ProjectConfiguration.RequestedPack> selectedRequestedRoots,
        ProjectConfiguration.RequestedPack updateRequestRoot,
        IReadOnlyList<UpdateOutcome> outcomes,
        bool dryRun,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources,
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
            SkipInstructions = skipInstructions,
            AcceptSources = acceptSources,
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

        PackUpdatePlan? appliedPlan = null;
        var exitCode = await packLifecycleService.UpdateAsync(
            projectDirectory,
            selectedRequestedRoots,
            updateRequest,
            plan => appliedPlan = plan
        );
        return exitCode == 0
            ? UpdateResult.Success(outcomes, appliedPlan)
            : UpdateResult.LifecycleFailure();
    }

    internal sealed record UpdateResult(
        IReadOnlyList<UpdateOutcome> Outcomes,
        string? Error,
        bool IsLifecycleFailure,
        PackUpdatePlan? FileChangePlan,
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
