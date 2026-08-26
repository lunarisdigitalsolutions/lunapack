using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace Lunapack.Cli;

internal sealed class PackLifecycleService(
    IFileSystem fileSystem,
    CompositePackGraphResolver graphResolver,
    PackInstallationPlanner installationPlanner,
    PackUpdatePlanner updatePlanner,
    PackUpdateTransaction updateTransaction,
    IProjectStateStore projectStateStore,
    CliConsole console,
    GitPackMaterializer? configuredGitPackMaterializer = null,
    LifecycleHookPlanner? configuredHookPlanner = null,
    LifecycleHookAuthorizer? configuredHookAuthorizer = null,
    LifecycleHookExecutor? configuredHookExecutor = null
)
{
    private static readonly UTF8Encoding _utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    private readonly GitPackMaterializer _gitPackMaterializer =
        configuredGitPackMaterializer
        ?? new GitPackMaterializer(fileSystem, new GitProcessRunner());
    private readonly CliConsole _console = console;
    private readonly LifecycleHookPlanner _hookPlanner = configuredHookPlanner ?? new(fileSystem);
    private readonly LifecycleHookAuthorizer _hookAuthorizer =
        configuredHookAuthorizer
        ?? new(
            new UserSettingsStore(fileSystem),
            new TrustPolicy(fileSystem),
            new LifecycleCommandResolver(fileSystem),
            new ConsoleLifecycleHookConfirmer(console)
        );
    private readonly LifecycleHookExecutor _hookExecutor =
        configuredHookExecutor ?? new(fileSystem, console);

    public async Task<int> InstallAsync(
        string projectDirectory,
        PackInstallationRequest installationRequest
    )
    {
        _console.Info($"Installing pack '{installationRequest.PackReference.Id}'.");
        var preparation = await PrepareInstallationAsync(projectDirectory, installationRequest);
        if (preparation.Value is not { } preparedInstallation)
        {
            return _console.Fail(preparation.Error);
        }

        await using (preparedInstallation.Materialization)
        {
            var hooks = await AuthorizeHooksAsync(
                projectDirectory,
                preparedInstallation.State,
                preparedInstallation.Configuration,
                preparedInstallation.Graph,
                preparedInstallation.Parameters,
                installationRequest.ScriptMode
            );
            if (hooks.Value is not { } authorizedHooks)
            {
                return _console.Fail(hooks.Error);
            }

            return await ApplyUpdateAndSaveAsync(
                preparedInstallation.State,
                preparedInstallation.Configuration,
                preparedInstallation.Graph,
                preparedInstallation.InstallationPlan,
                preparedInstallation.UpdatePlan,
                projectDirectory,
                preserveExistingLock: true,
                authorizedHooks
            );
        }
    }

    public async Task<ManifestOperationResult<PackInstallDryRunResult>> DryRunInstallAsync(
        string projectDirectory,
        PackInstallationRequest installationRequest
    )
    {
        var preparation = await PrepareInstallationAsync(projectDirectory, installationRequest);
        if (preparation.Value is not { } preparedInstallation)
        {
            return ManifestOperationResult<PackInstallDryRunResult>.Failure(
                preparation.Error ?? "Unable to plan pack installation."
            );
        }

        await using (preparedInstallation.Materialization)
        {
            var lifecycle = CreateDryRunLifecyclePlan(
                preparedInstallation.State,
                preparedInstallation.Graph,
                preparedInstallation.Parameters,
                installationRequest.ScriptMode
            );
            if (lifecycle.Value is not { } dryRunLifecycle)
            {
                return ManifestOperationResult<PackInstallDryRunResult>.Failure(
                    lifecycle.Error ?? "Unable to plan lifecycle hooks."
                );
            }

            return ManifestOperationResult<PackInstallDryRunResult>.Success(
                new PackInstallDryRunResult(
                    preparedInstallation.SelectedRelease,
                    preparedInstallation.UpdatePlan with
                    {
                        Lifecycle = dryRunLifecycle,
                    }
                )
            );
        }
    }

    public async Task<ManifestOperationResult<bool>> IsRequestedRootInstalledAsync(
        string projectDirectory,
        PackReference packReference
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<bool>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        return ManifestOperationResult<bool>.Success(
            state.Configuration.Packs.Exists(pack =>
                string.Equals(pack.Id, packReference.Id, StringComparison.Ordinal)
            )
        );
    }

    public async Task<int> MoveManagedFileAsync(
        string projectDirectory,
        string sourcePath,
        string targetPath
    )
    {
        var moveRequest = CreateManagedFileMoveRequest(projectDirectory, sourcePath, targetPath);
        if (moveRequest.Value is not { } request)
        {
            return _console.Fail(moveRequest.Error);
        }

        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return _console.Fail(loadedState.Error);
        }

        var owner = FindManagedFileMoveOwner(state.LockFile, request);
        if (owner.Value is not { } managedFileOwner)
        {
            return _console.Fail(owner.Error);
        }

        return await ApplyManagedFileMoveAndSaveAsync(
            projectDirectory,
            state,
            state.LockFile,
            managedFileOwner,
            request
        );
    }

    private ManifestOperationResult<ManagedFileMoveRequest> CreateManagedFileMoveRequest(
        string projectDirectory,
        string sourcePath,
        string targetPath
    )
    {
        var source = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            sourcePath
        );
        if (source.Value is not { } normalizedSource)
        {
            return ManifestOperationResult<ManagedFileMoveRequest>.Failure(
                $"Invalid managed file source '{sourcePath}': {source.Error}"
            );
        }

        var target = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            targetPath
        );
        if (target.Value is not { } normalizedTarget)
        {
            return ManifestOperationResult<ManagedFileMoveRequest>.Failure(
                $"Invalid managed file target '{targetPath}': {target.Error}"
            );
        }

        return string.Equals(normalizedSource, normalizedTarget, StringComparison.Ordinal)
            ? ManifestOperationResult<ManagedFileMoveRequest>.Failure(
                "Managed file source and target must differ."
            )
            : ManifestOperationResult<ManagedFileMoveRequest>.Success(
                new ManagedFileMoveRequest(normalizedSource, normalizedTarget)
            );
    }

    private static ManifestOperationResult<ProjectLockFile.ManagedFile> FindManagedFileMoveOwner(
        ProjectLockFile lockFile,
        ManagedFileMoveRequest request
    )
    {
        var sourceOwners = lockFile
            .Packs.SelectMany(pack => pack.ManagedFiles)
            .Where(file =>
                string.Equals(
                    NormalizePath(file.TargetPath),
                    request.SourcePath,
                    StringComparison.Ordinal
                )
            )
            .ToList();
        if (sourceOwners.Count != 1)
        {
            return ManifestOperationResult<ProjectLockFile.ManagedFile>.Failure(
                $"Managed file source '{request.SourcePath}' must be owned by exactly one lock record."
            );
        }

        return lockFile
            .Packs.SelectMany(pack => pack.ManagedFiles)
            .Any(file =>
                string.Equals(
                    NormalizePath(file.TargetPath),
                    request.TargetPath,
                    StringComparison.Ordinal
                )
            )
            ? ManifestOperationResult<ProjectLockFile.ManagedFile>.Failure(
                $"Managed file target '{request.TargetPath}' is already owned."
            )
            : ManifestOperationResult<ProjectLockFile.ManagedFile>.Success(sourceOwners[0]);
    }

    private async Task<int> ApplyManagedFileMoveAndSaveAsync(
        string projectDirectory,
        ProjectState state,
        ProjectLockFile lockFile,
        ProjectLockFile.ManagedFile managedFile,
        ManagedFileMoveRequest request
    )
    {
        var sourceFilePath = fileSystem.Path.GetFullPath(request.SourcePath, projectDirectory);
        var targetFilePath = fileSystem.Path.GetFullPath(request.TargetPath, projectDirectory);
        var sourceExists = fileSystem.File.Exists(sourceFilePath);
        var targetExists = fileSystem.File.Exists(targetFilePath);
        if (sourceExists == targetExists)
        {
            return _console.Fail(
                "Managed file move requires an existing source and missing target, or a missing source and existing target."
            );
        }

        var movedFile = false;
        var createdDirectories = new List<string>();
        try
        {
            if (sourceExists)
            {
                createdDirectories = CreateTargetDirectories(targetFilePath);
                fileSystem.File.Move(sourceFilePath, targetFilePath);
                movedFile = true;
            }

            managedFile.TargetPath = request.TargetPath;
            var savedState = await projectStateStore.SaveAsync(
                projectDirectory,
                state with
                {
                    LockFile = lockFile,
                }
            );
            if (savedState.IsSuccess)
            {
                return 0;
            }

            RestoreManagedFileMove(sourceFilePath, targetFilePath, movedFile, createdDirectories);
            return _console.Fail(savedState.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreManagedFileMove(sourceFilePath, targetFilePath, movedFile, createdDirectories);
            return _console.Fail($"Unable to move managed file: {exception.Message}");
        }
    }

    public async Task<
        ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>
    > FindUnresolvedRequiredParametersAsync(
        string projectDirectory,
        PackInstallationRequest installationRequest
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        var requestedPack = installationRequest.PackReference;
        if (
            state.Configuration.Packs.Exists(pack =>
                string.Equals(pack.Id, requestedPack.Id, StringComparison.Ordinal)
            )
        )
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                $"Pack '{requestedPack.Id}' is already installed."
            );
        }

        var graph = await ResolveUninstalledGraphAsync(
            projectDirectory,
            state.Configuration,
            requestedPack,
            state.LockFile
        );
        return graph.Value is { } resolvedGraph
            ? PackParameterResolver.FindUnresolvedRequired(
                resolvedGraph,
                state.Configuration,
                installationRequest
            )
            : ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                graph.Error ?? "Unable to resolve pack graph.",
                graph.ErrorKind
            );
    }

    private async Task<ManifestOperationResult<ResolvedPackGraph>> ResolveUninstalledGraphAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        PackReference packReference,
        ProjectLockFile lockFile
    )
    {
        var graph = await graphResolver.ResolveAsync(
            projectDirectory,
            configuration,
            packReference.Id,
            packReference.Version
        );
        if (graph.Value is not { } resolvedGraph)
        {
            return ManifestOperationResult<ResolvedPackGraph>.Failure(
                graph.Error ?? "Unable to resolve pack graph.",
                graph.ErrorKind
            );
        }

        var uninstalledPacks = new List<DiscoveredPack>();
        foreach (var pack in resolvedGraph.Packs)
        {
            var installedPack = lockFile.Packs.Find(lockPack =>
                string.Equals(lockPack.Id, pack.Manifest.Id, StringComparison.Ordinal)
            );
            if (installedPack is null)
            {
                uninstalledPacks.Add(pack);
                continue;
            }

            if (
                !string.Equals(
                    installedPack.Version,
                    pack.Manifest.Version,
                    StringComparison.Ordinal
                )
            )
            {
                return ManifestOperationResult<ResolvedPackGraph>.Failure(
                    $"Pack '{pack.Manifest.Id}' is already installed as version '{installedPack.Version}', which conflicts with version '{pack.Manifest.Version}'."
                );
            }
        }

        return ManifestOperationResult<ResolvedPackGraph>.Success(
            new ResolvedPackGraph(uninstalledPacks, resolvedGraph.RootPackIds)
        );
    }

    public async Task<int> UpdateAsync(
        string projectDirectory,
        IReadOnlyList<ProjectConfiguration.RequestedPack> selectedRequestedRoots,
        PackInstallationRequest updateRequest
    )
    {
        var preparation = await PrepareUpdateAsync(
            projectDirectory,
            selectedRequestedRoots,
            updateRequest
        );
        if (preparation.Value is not { } preparedUpdate)
        {
            return _console.Fail(preparation.Error);
        }

        await using (preparedUpdate.Materialization)
        {
            var hooks = await AuthorizeHooksAsync(
                projectDirectory,
                preparedUpdate.State,
                preparedUpdate.Configuration,
                preparedUpdate.Graph,
                preparedUpdate.Parameters,
                updateRequest.ScriptMode
            );
            if (hooks.Value is not { } authorizedHooks)
            {
                return _console.Fail(hooks.Error);
            }

            return await ApplyUpdateAndSaveAsync(
                preparedUpdate.State,
                preparedUpdate.Configuration,
                preparedUpdate.Graph,
                preparedUpdate.InstallationPlan,
                preparedUpdate.UpdatePlan,
                projectDirectory,
                authorizedHooks: authorizedHooks
            );
        }
    }

    public async Task<ManifestOperationResult<PackUpdatePlan>> DryRunUpdateAsync(
        string projectDirectory,
        IReadOnlyList<ProjectConfiguration.RequestedPack> selectedRequestedRoots,
        PackInstallationRequest updateRequest
    )
    {
        var preparation = await PrepareUpdateAsync(
            projectDirectory,
            selectedRequestedRoots,
            updateRequest
        );
        if (preparation.Value is not { } preparedUpdate)
        {
            return ManifestOperationResult<PackUpdatePlan>.Failure(
                preparation.Error ?? "Unable to plan pack update."
            );
        }

        await using (preparedUpdate.Materialization)
        {
            var lifecycle = CreateDryRunLifecyclePlan(
                preparedUpdate.State,
                preparedUpdate.Graph,
                preparedUpdate.Parameters,
                updateRequest.ScriptMode
            );
            return lifecycle.Value is { } dryRunLifecycle
                ? ManifestOperationResult<PackUpdatePlan>.Success(
                    preparedUpdate.UpdatePlan with
                    {
                        Lifecycle = dryRunLifecycle,
                    }
                )
                : ManifestOperationResult<PackUpdatePlan>.Failure(
                    lifecycle.Error ?? "Unable to plan lifecycle hooks."
                );
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Installation preparation coordinates state, graph, materialization, parameters, and plans."
    )]
    private async Task<ManifestOperationResult<PreparedPackInstallation>> PrepareInstallationAsync(
        string projectDirectory,
        PackInstallationRequest installationRequest
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<PreparedPackInstallation>.Failure(
                loadedState.Error ?? "An error occurred"
            );
        }

        var packReference = installationRequest.PackReference;
        if (
            state.Configuration.Packs.Exists(request =>
                string.Equals(request.Id, packReference.Id, StringComparison.Ordinal)
            )
        )
        {
            return ManifestOperationResult<PreparedPackInstallation>.Failure(
                $"Pack '{packReference.Id}' is already installed."
            );
        }

        var nextConfiguration = state.Configuration with
        {
            Packs =
            [
                .. state.Configuration.Packs,
                new ProjectConfiguration.RequestedPack
                {
                    Destination = installationRequest.Destination,
                    Id = packReference.Id,
                    Version = packReference.Version,
                },
            ],
        };
        var graph = await ResolveUninstalledGraphAsync(
            projectDirectory,
            nextConfiguration,
            packReference,
            state.LockFile
        );
        if (graph.Value is not { } resolvedGraph)
        {
            return ManifestOperationResult<PreparedPackInstallation>.Failure(
                graph.Error ?? "Unable to resolve pack graph."
            );
        }

        var materializationResult = await _gitPackMaterializer.MaterializeAsync(
            resolvedGraph,
            nextConfiguration
        );
        if (materializationResult.Value is not { } materialization)
        {
            return ManifestOperationResult<PreparedPackInstallation>.Failure(
                materializationResult.Error ?? "Unable to materialize Git packs."
            );
        }

        var retainMaterialization = false;
        try
        {
            var updatePlanningRequest = installationRequest with
            {
                PlanningMode = PackManagedFilePlanningMode.Update,
            };
            var parameterResolution = PackParameterResolver.Resolve(
                materialization.Graph,
                nextConfiguration,
                updatePlanningRequest
            );
            if (parameterResolution.Value is not { } resolvedParameters)
            {
                return ManifestOperationResult<PreparedPackInstallation>.Failure(
                    parameterResolution.Error ?? "Unable to resolve pack parameters."
                );
            }

            var installationPlan = installationPlanner.Plan(
                projectDirectory,
                materialization.Graph,
                state.LockFile,
                nextConfiguration,
                updatePlanningRequest,
                resolvedParameters
            );
            if (installationPlan.Value is not { } plan)
            {
                return ManifestOperationResult<PreparedPackInstallation>.Failure(
                    installationPlan.Error ?? "Unable to plan pack installation."
                );
            }

            var updatePlan = updatePlanner.Plan(
                projectDirectory,
                state.LockFile,
                plan,
                removeUnplannedManagedFiles: false
            );
            if (updatePlan.Value is not { } plannedUpdate)
            {
                return ManifestOperationResult<PreparedPackInstallation>.Failure(
                    updatePlan.Error ?? "Unable to plan managed-file update."
                );
            }

            var selectedPack = materialization.Graph.Packs.SingleOrDefault(pack =>
                string.Equals(pack.Manifest.Id, packReference.Id, StringComparison.Ordinal)
            );
            if (selectedPack is null)
            {
                return ManifestOperationResult<PreparedPackInstallation>.Failure(
                    $"Resolved graph does not contain requested pack '{packReference.Id}'."
                );
            }

            retainMaterialization = true;
            return ManifestOperationResult<PreparedPackInstallation>.Success(
                new PreparedPackInstallation(
                    state,
                    nextConfiguration,
                    materialization.Graph,
                    plan,
                    plannedUpdate,
                    resolvedParameters,
                    new PackReference(selectedPack.Manifest.Id, selectedPack.Manifest.Version),
                    materialization
                )
            );
        }
        finally
        {
            if (!retainMaterialization)
            {
                await materialization.DisposeAsync();
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Update preparation coordinates state, graph, materialization, parameters, and plans."
    )]
    private async Task<ManifestOperationResult<PreparedPackUpdate>> PrepareUpdateAsync(
        string projectDirectory,
        IReadOnlyList<ProjectConfiguration.RequestedPack> selectedRequestedRoots,
        PackInstallationRequest updateRequest
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<PreparedPackUpdate>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        var nextConfiguration = state.Configuration with { Packs = [.. selectedRequestedRoots] };
        var graph = await graphResolver.ResolveAsync(
            projectDirectory,
            nextConfiguration,
            nextConfiguration.Packs
        );
        if (graph.Value is not { } resolvedGraph)
        {
            return ManifestOperationResult<PreparedPackUpdate>.Failure(
                graph.Error ?? "Unable to resolve pack graph."
            );
        }

        var materializationResult = await _gitPackMaterializer.MaterializeAsync(
            resolvedGraph,
            nextConfiguration
        );
        if (materializationResult.Value is not { } materialization)
        {
            return ManifestOperationResult<PreparedPackUpdate>.Failure(
                materializationResult.Error ?? "Unable to materialize Git packs."
            );
        }

        var retainMaterialization = false;
        try
        {
            var updatePlanningRequest = updateRequest with
            {
                PlanningMode = PackManagedFilePlanningMode.Update,
            };
            var parameterResolution = PackParameterResolver.Resolve(
                materialization.Graph,
                nextConfiguration,
                updatePlanningRequest
            );
            if (parameterResolution.Value is not { } resolvedParameters)
            {
                return ManifestOperationResult<PreparedPackUpdate>.Failure(
                    parameterResolution.Error ?? "Unable to resolve pack parameters."
                );
            }

            var installationPlan = installationPlanner.Plan(
                projectDirectory,
                materialization.Graph,
                state.LockFile,
                nextConfiguration,
                updatePlanningRequest,
                resolvedParameters
            );
            if (installationPlan.Value is not { } plan)
            {
                return ManifestOperationResult<PreparedPackUpdate>.Failure(
                    installationPlan.Error ?? "Unable to plan pack installation."
                );
            }

            var updatePlan = updatePlanner.Plan(projectDirectory, state.LockFile, plan);
            if (updatePlan.Value is not { } plannedUpdate)
            {
                return ManifestOperationResult<PreparedPackUpdate>.Failure(
                    updatePlan.Error ?? "Unable to plan managed-file update."
                );
            }

            retainMaterialization = true;
            return ManifestOperationResult<PreparedPackUpdate>.Success(
                new PreparedPackUpdate(
                    state,
                    nextConfiguration,
                    materialization.Graph,
                    plan,
                    plannedUpdate,
                    resolvedParameters,
                    materialization
                )
            );
        }
        finally
        {
            if (!retainMaterialization)
            {
                await materialization.DisposeAsync();
            }
        }
    }

    public async Task<int> UninstallAsync(string projectDirectory, PackReference packReference)
    {
        _console.Info($"Uninstalling pack '{packReference.Id}'.");
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return _console.Fail(loadedState.Error ?? "Unable to load project state.");
        }

        var rootRequest = ValidateUninstallRequest(state, packReference);
        if (rootRequest.Value is not { } requestedRoot)
        {
            return _console.Fail(rootRequest.Error);
        }

        state.Configuration.Packs.Remove(requestedRoot);
        var nextLockFile = CreateRemainingLockFile(state.Configuration.Packs, state.LockFile);
        if (nextLockFile.Value is not { } lockFile)
        {
            return _console.Fail(nextLockFile.Error);
        }

        var removedPacks = GetRemovedPacks(state.LockFile, lockFile);
        var managedFilesToRemove = GetManagedFilesToRemove(removedPacks, lockFile);
        var changedFile = managedFilesToRemove.FirstOrDefault(managedFile =>
            ManagedTargetExists(managedFile.ManagedFile, projectDirectory)
            && !ManagedTargetIsUnchanged(managedFile.ManagedFile, projectDirectory)
        );
        if (changedFile is not null)
        {
            return _console.Fail(
                $"Managed target '{changedFile.ManagedFile.TargetPath}' has changed."
            );
        }

        return await DeleteAndSaveAsync(
            state with
            {
                LockFile = lockFile,
            },
            managedFilesToRemove,
            projectDirectory
        );
    }

    private static ManifestOperationResult<ProjectConfiguration.RequestedPack> ValidateUninstallRequest(
        ProjectState state,
        PackReference packReference
    )
    {
        var rootRequest = state.Configuration.Packs.Find(request =>
            string.Equals(request.Id, packReference.Id, StringComparison.Ordinal)
        );
        if (rootRequest is null)
        {
            return ManifestOperationResult<ProjectConfiguration.RequestedPack>.Failure(
                $"Pack '{packReference.Id}' is not installed."
            );
        }

        var installedPack = state.LockFile.Packs.Find(pack =>
            string.Equals(pack.Id, packReference.Id, StringComparison.Ordinal)
        );
        return
            packReference.Version is null
            || string.Equals(
                installedPack?.Version,
                packReference.Version,
                StringComparison.Ordinal
            )
            ? ManifestOperationResult<ProjectConfiguration.RequestedPack>.Success(rootRequest)
            : ManifestOperationResult<ProjectConfiguration.RequestedPack>.Failure(
                $"Installed pack '{packReference.Id}' is not version '{packReference.Version}'."
            );
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long"
    )]
    private async Task<int> ApplyUpdateAndSaveAsync(
        ProjectState state,
        ProjectConfiguration nextConfiguration,
        ResolvedPackGraph graph,
        PackInstallationPlan installationPlan,
        PackUpdatePlan updatePlan,
        string projectDirectory,
        bool preserveExistingLock = false,
        AuthorizedLifecycleHooks? authorizedHooks = null
    )
    {
        var manifestSnapshot = CreateManifestSnapshot(projectDirectory);
        var preExecution = await ExecuteHooksAsync(
            projectDirectory,
            authorizedHooks?.PreMutation ?? [],
            manifestSnapshot
        );
        if (!preExecution.IsSuccess)
        {
            return _console.Fail(preExecution.Error);
        }

        var appliedUpdate = updateTransaction.Apply(updatePlan);
        if (appliedUpdate.Value is not { } rollback)
        {
            return _console.Fail(appliedUpdate.Error);
        }

        var isPersisted = false;
        try
        {
            var postExecution = await ExecuteHooksAsync(
                projectDirectory,
                authorizedHooks?.PostMutation ?? [],
                manifestSnapshot
            );
            if (!postExecution.IsSuccess)
            {
                return _console.Fail(postExecution.Error);
            }

            var resultingContents = CreateResultingContents(updatePlan);
            var updatedLockFile = CreateLockFile(
                projectDirectory,
                nextConfiguration,
                graph,
                installationPlan,
                state.LockFile,
                resultingContents
            );
            var mergedLockFile = preserveExistingLock
                ? MergeLockFiles(state.LockFile, updatedLockFile)
                : ManifestOperationResult<ProjectLockFile>.Success(updatedLockFile);
            if (mergedLockFile.Value is not { } nextLockFile)
            {
                return _console.Fail(mergedLockFile.Error);
            }
            foreach (var (targetPath, contents) in resultingContents)
            {
                UpdateManagedFileHash(nextLockFile, targetPath, contents);
            }

            var nextState = state with
            {
                Configuration = nextConfiguration,
                LockFile = nextLockFile,
            };
            var savedState = await projectStateStore.SaveAsync(projectDirectory, nextState);
            if (savedState.IsSuccess)
            {
                isPersisted = true;
                return 0;
            }

            return _console.Fail(savedState.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return _console.Fail($"Unable to update pack: {exception.Message}");
        }
        finally
        {
            if (!isPersisted)
            {
                rollback.Restore();
            }
        }
    }

    private async Task<ManifestOperationResult<AuthorizedLifecycleHooks>> AuthorizeHooksAsync(
        string projectDirectory,
        ProjectState state,
        ProjectConfiguration configuration,
        ResolvedPackGraph graph,
        ResolvedPackParameters parameters,
        ScriptExecutionMode scriptMode
    )
    {
        var lifecyclePlan = PackLifecyclePlanner.Plan(graph, state.LockFile);
        var preHooks = _hookPlanner.PlanPreMutation(lifecyclePlan, parameters);
        var postHooks = _hookPlanner.PlanPostMutation(lifecyclePlan, parameters);
        if (
            preHooks.Value is not { } plannedPreHooks
            || postHooks.Value is not { } plannedPostHooks
        )
        {
            return ManifestOperationResult<AuthorizedLifecycleHooks>.Failure(
                preHooks.Error ?? postHooks.Error ?? "Unable to plan lifecycle hooks."
            );
        }

        var authorized = await _hookAuthorizer.AuthorizeAsync(
            projectDirectory,
            configuration,
            scriptMode,
            [.. plannedPreHooks, .. plannedPostHooks]
        );
        if (authorized.Value is not { } hooks)
        {
            return ManifestOperationResult<AuthorizedLifecycleHooks>.Failure(
                authorized.Error ?? "Unable to authorize lifecycle hooks."
            );
        }

        return ManifestOperationResult<AuthorizedLifecycleHooks>.Success(
            new AuthorizedLifecycleHooks(
                [
                    .. hooks.Where(hook =>
                        hook.Invocation.Hook is LifecycleHook.PreInstall or LifecycleHook.PreUpdate
                    ),
                ],
                [
                    .. hooks.Where(hook =>
                        hook.Invocation.Hook
                            is LifecycleHook.PostInstall
                                or LifecycleHook.PostUpdate
                    ),
                ]
            )
        );
    }

    private ManifestOperationResult<LifecycleDryRunPlan> CreateDryRunLifecyclePlan(
        ProjectState state,
        ResolvedPackGraph graph,
        ResolvedPackParameters parameters,
        ScriptExecutionMode scriptMode
    )
    {
        var lifecyclePlan = PackLifecyclePlanner.Plan(graph, state.LockFile);
        var preHooks = _hookPlanner.PlanPreMutation(lifecyclePlan, parameters);
        var postHooks = _hookPlanner.PlanPostMutation(lifecyclePlan, parameters);
        return preHooks.Value is { } plannedPreHooks && postHooks.Value is { } plannedPostHooks
            ? ManifestOperationResult<LifecycleDryRunPlan>.Success(
                new LifecycleDryRunPlan(
                    scriptMode,
                    plannedPreHooks,
                    plannedPostHooks,
                    lifecyclePlan.Changes
                )
            )
            : ManifestOperationResult<LifecycleDryRunPlan>.Failure(
                preHooks.Error ?? postHooks.Error ?? "Unable to plan lifecycle hooks."
            );
    }

    private async Task<ManifestOperationResult<bool>> ExecuteHooksAsync(
        string projectDirectory,
        IReadOnlyList<ResolvedLifecycleHookInvocation> hooks,
        ManifestSnapshot manifestSnapshot
    )
    {
        foreach (var hook in hooks)
        {
            var execution = await _hookExecutor.ExecuteAsync(projectDirectory, hook);
            var integrity = VerifyManifestSnapshot(manifestSnapshot);
            if (!integrity.IsSuccess)
            {
                return integrity;
            }

            if (!execution.IsSuccess)
            {
                return execution;
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private ManifestSnapshot CreateManifestSnapshot(string projectDirectory)
    {
        var path = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        return new ManifestSnapshot(path, fileSystem.File.ReadAllBytes(path));
    }

    private ManifestOperationResult<bool> VerifyManifestSnapshot(ManifestSnapshot snapshot)
    {
        if (
            fileSystem.File.Exists(snapshot.Path)
            && fileSystem.File.ReadAllBytes(snapshot.Path).SequenceEqual(snapshot.Contents)
        )
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        fileSystem.File.WriteAllBytes(snapshot.Path, snapshot.Contents);
        return ManifestOperationResult<bool>.Failure(
            $"Lifecycle hook changed '{ProjectStateStore.ConfigurationFileName}'; original contents were restored."
        );
    }

    private static Dictionary<string, byte[]> CreateResultingContents(PackUpdatePlan updatePlan)
    {
        var resultingContents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var action in updatePlan.Actions)
        {
            if (action.ResultingContents is { } contents)
            {
                resultingContents[action.TargetPathRelativeToProject] = contents;
            }
        }

        return resultingContents;
    }

    private static ManifestOperationResult<ProjectLockFile> MergeLockFiles(
        ProjectLockFile previousLockFile,
        ProjectLockFile updatedLockFile
    )
    {
        var updatedIds = updatedLockFile
            .Packs.Select(pack => pack.Id)
            .ToHashSet(StringComparer.Ordinal);
        return ManifestOperationResult<ProjectLockFile>.Success(
            new ProjectLockFile
            {
                SchemaVersion = 1,
                Packs =
                [
                    .. previousLockFile.Packs.Where(pack => !updatedIds.Contains(pack.Id)),
                    .. updatedLockFile.Packs,
                ],
            }
        );
    }

    private async Task<int> DeleteAndSaveAsync(
        ProjectState state,
        IReadOnlyList<ManagedFileRemoval> managedFilesToRemove,
        string projectDirectory
    )
    {
        var isPersisted = false;
        var snapshots = new List<ManagedFileSnapshot>();
        try
        {
            foreach (var removal in managedFilesToRemove)
            {
                var appliedRemoval = ApplyRemoval(
                    removal,
                    projectDirectory,
                    state.LockFile,
                    snapshots
                );
                if (!appliedRemoval.IsSuccess)
                {
                    return _console.Fail(appliedRemoval.Error ?? "Unable to remove managed file.");
                }
            }

            var savedState = await projectStateStore.SaveAllowingUnavailableSourcesAsync(
                projectDirectory,
                state
            );
            if (savedState.IsSuccess)
            {
                isPersisted = true;
                return 0;
            }

            return _console.Fail(savedState.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return _console.Fail($"Unable to uninstall pack: {exception.Message}");
        }
        finally
        {
            if (!isPersisted)
            {
                RestoreManagedFiles(snapshots);
            }
        }
    }

    private ManifestOperationResult<bool> ApplyRemoval(
        ManagedFileRemoval removal,
        string projectDirectory,
        ProjectLockFile lockFile,
        ICollection<ManagedFileSnapshot> snapshots
    )
    {
        var managedFile = removal.ManagedFile;
        var targetPath = fileSystem.Path.GetFullPath(managedFile.TargetPath, projectDirectory);
        if (!fileSystem.File.Exists(targetPath))
        {
            _console.Warning(
                $"Managed target '{managedFile.TargetPath}' is missing during uninstall; removing ownership state."
            );
            return ManifestOperationResult<bool>.Success(true);
        }

        var targetContents = fileSystem.File.ReadAllBytes(targetPath);
        snapshots.Add(new ManagedFileSnapshot(targetPath, targetContents));
        if (removal.Kind == ManagedFileRemovalKind.Delete)
        {
            fileSystem.File.Delete(targetPath);
            _console.Debug($"Deleted managed file '{managedFile.TargetPath}'.");
            return ManifestOperationResult<bool>.Success(true);
        }

        return ApplySectionRemoval(managedFile, targetPath, targetContents, lockFile);
    }

    private ManifestOperationResult<bool> ApplySectionRemoval(
        ProjectLockFile.ManagedFile managedFile,
        string targetPath,
        byte[] targetContents,
        ProjectLockFile lockFile
    )
    {
        var updatedContents = RemoveSection(targetContents, managedFile.Content);
        if (updatedContents.Value is not { } contents)
        {
            return ManifestOperationResult<bool>.Failure(
                updatedContents.Error ?? "Unable to remove managed section."
            );
        }

        fileSystem.File.WriteAllBytes(targetPath, contents);
        _console.Debug($"Removed managed section from '{managedFile.TargetPath}'.");
        UpdateManagedFileHash(lockFile, managedFile.TargetPath, contents);
        return ManifestOperationResult<bool>.Success(true);
    }

    private ProjectLockFile CreateLockFile(
        string projectDirectory,
        ProjectConfiguration configuration,
        ResolvedPackGraph graph,
        PackInstallationPlan installationPlan,
        ProjectLockFile previousLockFile,
        IReadOnlyDictionary<string, byte[]>? resultingContents = null
    )
    {
        var resolvedPacks = new List<ProjectLockFile.ResolvedPack>(graph.Packs.Count);
        foreach (var pack in graph.Packs)
        {
            var gitSource = pack.GitSource;
            var managedFiles = CreateLockManagedFiles(
                pack,
                installationPlan,
                previousLockFile,
                resultingContents
            );
            resolvedPacks.Add(
                new ProjectLockFile.ResolvedPack
                {
                    Destination = configuration
                        .Packs.Find(request =>
                            string.Equals(request.Id, pack.Manifest.Id, StringComparison.Ordinal)
                        )
                        ?.Destination,
                    GitSource = gitSource,
                    Id = pack.Manifest.Id,
                    Version = pack.Manifest.Version,
                    SourceName = pack.SourceName,
                    SourceIdentity = pack.SourceIdentity,
                    SourcePath = gitSource is null ? pack.SourceIdentity.Path : null,
                    PackPath = gitSource is null
                        ? NormalizePath(
                            fileSystem.Path.GetRelativePath(pack.SourcePath, pack.PackDirectory)
                        )
                        : pack.RepositoryPath
                            ?? throw new InvalidOperationException(
                                "Git-sourced packs require a repository-relative pack path."
                            ),
                    Packs =
                    [
                        .. pack.Manifest.Packs.Select(reference => new ProjectLockFile.PackReference
                        {
                            Id = reference.Id,
                            Version = reference.Version,
                        }),
                    ],
                    ManagedFiles = managedFiles,
                }
            );
        }

        return new ProjectLockFile { SchemaVersion = 1, Packs = resolvedPacks };
    }

    private static List<ProjectLockFile.ManagedFile> CreateLockManagedFiles(
        DiscoveredPack pack,
        PackInstallationPlan installationPlan,
        ProjectLockFile previousLockFile,
        IReadOnlyDictionary<string, byte[]>? resultingContents
    ) =>
        installationPlan
            .ManagedFiles.Where(managedFile => IsSamePack(managedFile.Pack, pack))
            .Select(managedFile =>
                CreateLockManagedFile(pack, managedFile, previousLockFile, resultingContents)
            )
            .ToList();

    private static ProjectLockFile.ManagedFile CreateLockManagedFile(
        DiscoveredPack pack,
        PlannedManagedFile managedFile,
        ProjectLockFile previousLockFile,
        IReadOnlyDictionary<string, byte[]>? resultingContents
    )
    {
        var previousManagedFile = previousLockFile
            .Packs.Find(lockPack => IsSamePackId(lockPack, pack))
            ?.ManagedFiles.Find(file =>
                string.Equals(
                    file.DeclaredTargetPath ?? file.TargetPath,
                    managedFile.DeclaredTargetPath,
                    StringComparison.Ordinal
                )
            );
        var effectiveTargetPath =
            previousManagedFile?.TargetPath ?? managedFile.TargetPathRelativeToProject;

        return new ProjectLockFile.ManagedFile
        {
            Content = IsSectionMerge(managedFile.Strategy)
                ? Convert.ToBase64String(managedFile.Contents)
                : null,
            DeclaredTargetPath = managedFile.DeclaredTargetPath,
            TargetPath = effectiveTargetPath,
            Sha256 = ComputeSha256(
                resultingContents?.GetValueOrDefault(effectiveTargetPath) ?? managedFile.Contents
            ),
            Strategy = new ProjectLockFile.ManagedFileStrategy
            {
                Method = managedFile.Strategy.Method,
                Type = managedFile.Strategy.Type,
            },
        };
    }

    private static ManifestOperationResult<ProjectLockFile> CreateRemainingLockFile(
        IReadOnlyList<ProjectConfiguration.RequestedPack> requestedRoots,
        ProjectLockFile previousLockFile
    )
    {
        var packsById = previousLockFile.Packs.ToDictionary(
            pack => pack.Id,
            StringComparer.Ordinal
        );
        var remainingIds = new HashSet<string>(StringComparer.Ordinal);
        var pendingIds = new Stack<string>(requestedRoots.Select(pack => pack.Id));
        while (pendingIds.TryPop(out var packId))
        {
            if (!remainingIds.Add(packId))
            {
                continue;
            }

            if (!packsById.TryGetValue(packId, out var resolvedPack))
            {
                return ManifestOperationResult<ProjectLockFile>.Failure(
                    $"Lock file does not contain resolved pack '{packId}'."
                );
            }

            foreach (var dependency in resolvedPack.Packs)
            {
                if (
                    !packsById.TryGetValue(dependency.Id, out var resolvedDependency)
                    || !string.Equals(
                        dependency.Version,
                        resolvedDependency.Version,
                        StringComparison.Ordinal
                    )
                )
                {
                    return ManifestOperationResult<ProjectLockFile>.Failure(
                        $"Lock file does not contain resolved pack '{dependency.Id}@{dependency.Version}'."
                    );
                }

                pendingIds.Push(dependency.Id);
            }
        }

        return ManifestOperationResult<ProjectLockFile>.Success(
            new ProjectLockFile
            {
                SchemaVersion = 1,
                Packs = previousLockFile
                    .Packs.Where(pack => remainingIds.Contains(pack.Id))
                    .ToList(),
            }
        );
    }

    private static ProjectLockFile.ManagedFile? FindManagedFile(
        ProjectLockFile lockFile,
        DiscoveredPack pack,
        string targetPath
    ) =>
        lockFile
            .Packs.Find(lockPack => IsSamePack(lockPack, pack))
            ?.ManagedFiles.Find(file =>
                string.Equals(file.TargetPath, targetPath, StringComparison.Ordinal)
            );

    private static List<ProjectLockFile.ResolvedPack> GetRemovedPacks(
        ProjectLockFile previousLockFile,
        ProjectLockFile remainingLockFile
    ) =>
        previousLockFile
            .Packs.Where(lockPack =>
                !remainingLockFile.Packs.Exists(pack =>
                    string.Equals(pack.Id, lockPack.Id, StringComparison.Ordinal)
                )
            )
            .ToList();

    private static List<ManagedFileRemoval> GetManagedFilesToRemove(
        IReadOnlyList<ProjectLockFile.ResolvedPack> removedPacks,
        ProjectLockFile remainingLockFile
    )
    {
        var remainingTargets = remainingLockFile
            .Packs.SelectMany(pack => pack.ManagedFiles)
            .Select(managedFile => NormalizePath(managedFile.TargetPath))
            .ToHashSet(StringComparer.Ordinal);
        var removals = new List<ManagedFileRemoval>();
        foreach (var managedFile in removedPacks.SelectMany(pack => pack.ManagedFiles))
        {
            var removalKind = GetRemovalKind(managedFile);
            if (
                removalKind is not null
                && (
                    removalKind == ManagedFileRemovalKind.RemoveSection
                    || !remainingTargets.Contains(NormalizePath(managedFile.TargetPath))
                )
            )
            {
                removals.Add(new ManagedFileRemoval(managedFile, removalKind.Value));
            }
        }

        return removals;
    }

    private static ManagedFileRemovalKind? GetRemovalKind(
        ProjectLockFile.ManagedFile managedFile
    ) =>
        managedFile.Strategy switch
        {
            { Type: "merge", Method: "section" } => ManagedFileRemovalKind.RemoveSection,
            { Type: "merge" } => null,
            _ => ManagedFileRemovalKind.Delete,
        };

    private static bool IsSectionMerge(PackManifest.PackManagedFileStrategy strategy) =>
        string.Equals(strategy.Type, "merge", StringComparison.Ordinal)
        && string.Equals(strategy.Method, "section", StringComparison.Ordinal);

    private static ManifestOperationResult<byte[]> RemoveSection(
        byte[] targetContents,
        string? encodedSectionContents
    )
    {
        var sectionLines = DecodeSectionLines(encodedSectionContents);
        if (sectionLines.Value is not { } markers)
        {
            return ManifestOperationResult<byte[]>.Failure(
                sectionLines.Error ?? "Managed section content is unavailable in the lock file."
            );
        }

        try
        {
            return RemoveSectionFromText(_utf8.GetString(targetContents), markers);
        }
        catch (DecoderFallbackException exception)
        {
            return ManifestOperationResult<byte[]>.Failure(
                $"Managed section cannot be removed: {exception.Message}"
            );
        }
    }

    private static ManifestOperationResult<List<string>> DecodeSectionLines(
        string? encodedSectionContents
    )
    {
        if (encodedSectionContents is null)
        {
            return ManifestOperationResult<List<string>>.Failure(
                "Managed section content is unavailable in the lock file."
            );
        }

        try
        {
            var sectionLines = ReadLines(
                _utf8.GetString(Convert.FromBase64String(encodedSectionContents))
            );
            return sectionLines.Count < 2
                ? ManifestOperationResult<List<string>>.Failure(
                    "Managed section requires distinct first and last marker lines."
                )
                : ManifestOperationResult<List<string>>.Success(sectionLines);
        }
        catch (Exception exception) when (exception is DecoderFallbackException or FormatException)
        {
            return ManifestOperationResult<List<string>>.Failure(
                $"Managed section cannot be decoded: {exception.Message}"
            );
        }
    }

    private static ManifestOperationResult<byte[]> RemoveSectionFromText(
        string targetText,
        List<string> sectionLines
    )
    {
        var targetLines = ReadLines(targetText);
        var firstMarkerIndexes = FindMarkerIndexes(targetLines, sectionLines[0]);
        var lastMarkerIndexes = FindMarkerIndexes(targetLines, sectionLines[^1]);
        if (
            firstMarkerIndexes.Count != 1
            || lastMarkerIndexes.Count != 1
            || firstMarkerIndexes[0] >= lastMarkerIndexes[0]
        )
        {
            return ManifestOperationResult<byte[]>.Failure(
                "Managed section markers are incomplete or ambiguous."
            );
        }

        targetLines.RemoveRange(
            firstMarkerIndexes[0],
            lastMarkerIndexes[0] - firstMarkerIndexes[0] + 1
        );
        var newLine = targetText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var updatedText = string.Join(newLine, targetLines);
        if (targetText.EndsWith('\n') && targetLines.Count > 0)
        {
            updatedText += newLine;
        }

        return ManifestOperationResult<byte[]>.Success(_utf8.GetBytes(updatedText));
    }

    private static List<int> FindMarkerIndexes(IReadOnlyList<string> lines, string marker) =>
        lines
            .Select((line, index) => new { line, index })
            .Where(item => string.Equals(item.line, marker, StringComparison.Ordinal))
            .Select(item => item.index)
            .ToList();

    private static List<string> ReadLines(string text)
    {
        var lines = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static void UpdateManagedFileHash(
        ProjectLockFile lockFile,
        string targetPath,
        byte[] contents
    )
    {
        var normalizedTargetPath = NormalizePath(targetPath);
        foreach (var managedFile in lockFile.Packs.SelectMany(pack => pack.ManagedFiles))
        {
            if (
                string.Equals(
                    NormalizePath(managedFile.TargetPath),
                    normalizedTargetPath,
                    StringComparison.Ordinal
                )
            )
            {
                managedFile.Sha256 = ComputeSha256(contents);
            }
        }
    }

    private bool ManagedTargetIsUnchanged(
        ProjectLockFile.ManagedFile managedFile,
        string projectDirectory
    )
    {
        var targetPath = fileSystem.Path.GetFullPath(managedFile.TargetPath, projectDirectory);
        return string.Equals(
            ComputeSha256(targetPath),
            managedFile.Sha256,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private bool ManagedTargetExists(
        ProjectLockFile.ManagedFile managedFile,
        string projectDirectory
    ) =>
        fileSystem.File.Exists(
            fileSystem.Path.GetFullPath(managedFile.TargetPath, projectDirectory)
        );

    private void RestoreManagedFiles(IReadOnlyList<ManagedFileSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            fileSystem.File.WriteAllBytes(snapshot.Path, snapshot.Contents);
        }
    }

    private string ComputeSha256(string path)
    {
        using var stream = fileSystem.File.OpenRead(path);

        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ComputeSha256(byte[] contents) =>
        Convert.ToHexString(SHA256.HashData(contents));

    private List<string> CreateTargetDirectories(string targetPath)
    {
        var targetDirectory = fileSystem.Path.GetDirectoryName(targetPath);
        var missingDirectories = new Stack<string>();
        while (targetDirectory is not null && !fileSystem.Directory.Exists(targetDirectory))
        {
            missingDirectories.Push(targetDirectory);
            targetDirectory = fileSystem.Path.GetDirectoryName(targetDirectory);
        }

        var createdDirectories = new List<string>(missingDirectories.Count);
        foreach (var directory in missingDirectories)
        {
            fileSystem.Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
        }

        return createdDirectories;
    }

    private void RestoreManagedFileMove(
        string sourcePath,
        string targetPath,
        bool movedFile,
        IReadOnlyList<string> createdDirectories
    )
    {
        if (movedFile && fileSystem.File.Exists(targetPath))
        {
            fileSystem.File.Move(targetPath, sourcePath);
        }

        foreach (var directory in createdDirectories.Reverse())
        {
            if (
                fileSystem.Directory.Exists(directory)
                && !fileSystem.Directory.EnumerateFileSystemEntries(directory).Any()
            )
            {
                fileSystem.Directory.Delete(directory);
            }
        }
    }

    private static bool IsSamePack(ProjectLockFile.ResolvedPack lockPack, DiscoveredPack pack) =>
        IsSamePackId(lockPack, pack)
        && string.Equals(lockPack.Version, pack.Manifest.Version, StringComparison.Ordinal);

    private static bool IsSamePackId(ProjectLockFile.ResolvedPack lockPack, DiscoveredPack pack) =>
        string.Equals(lockPack.Id, pack.Manifest.Id, StringComparison.Ordinal);

    private static bool IsSamePack(DiscoveredPack first, DiscoveredPack second) =>
        string.Equals(first.Manifest.Id, second.Manifest.Id, StringComparison.Ordinal)
        && string.Equals(first.Manifest.Version, second.Manifest.Version, StringComparison.Ordinal);

    private static string NormalizePath(string path) => ProjectPath.Normalize(path);

    private sealed record ManagedFileMoveRequest(string SourcePath, string TargetPath);

    private sealed record ManagedFileSnapshot(string Path, byte[] Contents);

    private sealed record ManifestSnapshot(string Path, byte[] Contents);

    private sealed record ManagedFileRemoval(
        ProjectLockFile.ManagedFile ManagedFile,
        ManagedFileRemovalKind Kind
    );

    private enum ManagedFileRemovalKind
    {
        Delete,
        RemoveSection,
    }
}
