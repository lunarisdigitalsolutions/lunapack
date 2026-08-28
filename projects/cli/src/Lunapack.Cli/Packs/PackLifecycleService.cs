using System.Diagnostics;
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
    LifecycleHookExecutor? configuredHookExecutor = null,
    ExternalSourceRequirementPlanner? configuredExternalSourceRequirementPlanner = null,
    ExternalSourceMaterializer? configuredExternalSourceMaterializer = null,
    ExternalSourceConsentCoordinator? configuredExternalSourceConsentCoordinator = null
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
    private readonly ExternalSourceRequirementPlanner _externalSourceRequirementPlanner =
        configuredExternalSourceRequirementPlanner
        ?? new(new GitRefResolver(new GitProcessRunner()), new ManagedFileConditionParser());
    private readonly ExternalSourceMaterializer _externalSourceMaterializer =
        configuredExternalSourceMaterializer
        ?? new(fileSystem, new GitProcessRunner(), new GitRefResolver(new GitProcessRunner()));
    private readonly ExternalSourceConsentCoordinator _externalSourceConsentCoordinator =
        configuredExternalSourceConsentCoordinator
        ?? new(
            console.IsInteractive
                ? new ConsoleExternalSourceApprover(console)
                : new DenyExternalSourceApprover(),
            console.IsInteractive
                ? new ConsoleExternalSourceIdentifierPrompter(console)
                : new DenyExternalSourceIdentifierPrompter()
        );

    public async Task<int> InstallAsync(
        string projectDirectory,
        PackInstallationRequest installationRequest,
        Action<TimeSpan>? onManagedFileChangesApplied = null
    )
    {
        _console.Info($"Installing pack '{installationRequest.PackReference.Id}'.");
        var preparation = await PrepareInstallationAsync(projectDirectory, installationRequest);
        if (preparation.Value is not { } preparedInstallation)
        {
            return _console.Fail(preparation.Error);
        }

        WriteManagedFileTemplateDiagnostics(preparedInstallation.InstallationPlan);

        await using (preparedInstallation.Materialization)
        await using (preparedInstallation.ExternalMaterialization)
        {
            var hooks = await AuthorizeHooksAsync(
                projectDirectory,
                preparedInstallation.State,
                preparedInstallation.Configuration,
                preparedInstallation.Graph,
                preparedInstallation.Parameters,
                installationRequest.ScriptMode,
                installationRequest.SkipInstructions
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
                authorizedHooks,
                onManagedFileChangesApplied
            );
        }
    }

    public async Task<ManifestOperationResult<PackInstallDryRunResult>> DryRunInstallAsync(
        string projectDirectory,
        PackInstallationRequest installationRequest
    )
    {
        var preparation = await PrepareInstallationAsync(
            projectDirectory,
            installationRequest,
            previewSources: true
        );
        if (preparation.Value is not { } preparedInstallation)
        {
            return ManifestOperationResult<PackInstallDryRunResult>.Failure(
                preparation.Error ?? "Unable to plan pack installation."
            );
        }

        WriteManagedFileTemplateDiagnostics(preparedInstallation.InstallationPlan);

        await using (preparedInstallation.Materialization)
        await using (preparedInstallation.ExternalMaterialization)
        {
            var lifecycle = await CreateDryRunLifecyclePlanAsync(
                projectDirectory,
                preparedInstallation.State,
                preparedInstallation.Configuration,
                preparedInstallation.Graph,
                preparedInstallation.Parameters,
                installationRequest.ScriptMode,
                installationRequest.SkipInstructions
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
                        ExternalSources = preparedInstallation.ExternalSources,
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

    public async Task<ManifestOperationResult<string>> GetInstalledVersionAsync(
        string projectDirectory,
        string packId
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<string>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        var installedPack = state.LockFile.Packs.Find(pack =>
            string.Equals(pack.Id, packId, StringComparison.Ordinal)
        );
        return installedPack is null
            ? ManifestOperationResult<string>.Failure(
                $"Installed pack '{packId}' is missing from the lock file."
            )
            : ManifestOperationResult<string>.Success(installedPack.Version);
    }

    public async Task<int> MoveManagedFileAsync(
        string projectDirectory,
        string sourcePath,
        string targetPath,
        bool saveRemapping = false
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

        var selection = FindManagedFileMoveSelection(state.LockFile, request);
        if (selection.Value is not { } managedFiles)
        {
            return _console.Fail(selection.Error);
        }

        return await ApplyManagedFileMovesAndSaveAsync(
            projectDirectory,
            state,
            state.LockFile,
            managedFiles,
            request,
            saveRemapping
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

    private static ManifestOperationResult<ManagedFileMoveSelection> FindManagedFileMoveSelection(
        ProjectLockFile lockFile,
        ManagedFileMoveRequest request
    )
    {
        var managedFiles = lockFile
            .Packs.SelectMany(pack => pack.ManagedFiles)
            .Select(file => new ManagedMoveFile(
                file.DeclaredTargetPath,
                file.TargetPath,
                targetPath => file.TargetPath = targetPath
            ))
            .Concat(
                lockFile
                    .Links.Values.SelectMany(link => link.Files)
                    .Select(file => new ManagedMoveFile(
                        file.DeclaredTargetPath,
                        file.TargetPath,
                        targetPath => file.TargetPath = targetPath
                    ))
            )
            .ToList();
        var exactOwners = managedFiles
            .Where(file =>
                string.Equals(
                    NormalizePath(file.TargetPath),
                    request.SourcePath,
                    StringComparison.Ordinal
                )
            )
            .ToList();
        if (exactOwners.Count > 1)
        {
            return ManifestOperationResult<ManagedFileMoveSelection>.Failure(
                $"Managed file source '{request.SourcePath}' must be owned by exactly one lock record."
            );
        }

        if (exactOwners.Count == 1)
        {
            var move = new ManagedFileMove(exactOwners[0], request.SourcePath, request.TargetPath);
            return ValidateManagedFileMoveTargets(managedFiles, [move], isDirectory: false);
        }

        var sourcePrefix = $"{request.SourcePath}/";
        var directoryMoves = managedFiles
            .Where(file =>
                NormalizePath(file.TargetPath).StartsWith(sourcePrefix, StringComparison.Ordinal)
            )
            .Select(file =>
            {
                var normalizedTarget = NormalizePath(file.TargetPath);
                return new ManagedFileMove(
                    file,
                    normalizedTarget,
                    $"{request.TargetPath}/{normalizedTarget[sourcePrefix.Length..]}"
                );
            })
            .ToList();
        if (directoryMoves.Count == 0)
        {
            return ManifestOperationResult<ManagedFileMoveSelection>.Failure(
                $"Managed file source '{request.SourcePath}' must identify one lock record or a directory containing managed files."
            );
        }

        if (
            request.TargetPath.StartsWith(sourcePrefix, StringComparison.Ordinal)
            || request.SourcePath.StartsWith($"{request.TargetPath}/", StringComparison.Ordinal)
        )
        {
            return ManifestOperationResult<ManagedFileMoveSelection>.Failure(
                "Managed directory source and target must not contain one another."
            );
        }

        return ValidateManagedFileMoveTargets(managedFiles, directoryMoves, isDirectory: true);
    }

    private static ManifestOperationResult<ManagedFileMoveSelection> ValidateManagedFileMoveTargets(
        IReadOnlyList<ManagedMoveFile> managedFiles,
        IReadOnlyList<ManagedFileMove> moves,
        bool isDirectory
    )
    {
        if (
            moves.Select(move => move.TargetPath).Distinct(StringComparer.Ordinal).Count()
            != moves.Count
        )
        {
            return ManifestOperationResult<ManagedFileMoveSelection>.Failure(
                "Managed file move produces duplicate targets."
            );
        }

        var selectedFiles = moves
            .Select(move => move.ManagedFile)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var ownedTargets = managedFiles
            .Where(file => !selectedFiles.Contains(file))
            .Select(file => NormalizePath(file.TargetPath))
            .ToHashSet(StringComparer.Ordinal);
        var conflict = moves.FirstOrDefault(move => ownedTargets.Contains(move.TargetPath));
        return conflict is null
            ? ManifestOperationResult<ManagedFileMoveSelection>.Success(
                new ManagedFileMoveSelection(moves, isDirectory)
            )
            : ManifestOperationResult<ManagedFileMoveSelection>.Failure(
                $"Managed file target '{conflict.TargetPath}' is already owned."
            );
    }

    private async Task<int> ApplyManagedFileMovesAndSaveAsync(
        string projectDirectory,
        ProjectState state,
        ProjectLockFile lockFile,
        ManagedFileMoveSelection selection,
        ManagedFileMoveRequest request,
        bool saveRemapping
    )
    {
        var operations = selection
            .Moves.Select(move => CreateManagedFileMoveOperation(projectDirectory, move))
            .ToList();
        var operationError = ValidateManagedFileMoveOperations(operations);
        if (operationError is not null)
        {
            return _console.Fail(operationError);
        }

        var createdDirectories = new List<string>();
        var movedOperations = new List<ManagedFileMoveOperation>();
        try
        {
            foreach (var operation in operations.Where(operation => operation.SourceExists))
            {
                createdDirectories.AddRange(CreateTargetDirectories(operation.TargetFilePath));
                fileSystem.File.Move(operation.SourceFilePath, operation.TargetFilePath);
                movedOperations.Add(operation);
            }

            foreach (var move in selection.Moves)
            {
                move.ManagedFile.SetTargetPath(move.TargetPath);
            }

            var configuration = saveRemapping
                ? AddSavedMoveRemapping(state.Configuration, selection, request)
                : ManifestOperationResult<ProjectConfiguration>.Success(state.Configuration);
            if (configuration.Value is not { } nextConfiguration)
            {
                RestoreManagedFileMoves(movedOperations, createdDirectories);
                RestoreManagedFileMoveTargets(selection.Moves);
                return _console.Fail(configuration.Error);
            }

            var savedState = await projectStateStore.SaveAsync(
                projectDirectory,
                state with
                {
                    Configuration = nextConfiguration,
                    LockFile = lockFile,
                }
            );
            if (savedState.IsSuccess)
            {
                if (selection.IsDirectory)
                {
                    try
                    {
                        RemoveEmptyMovedDirectories(projectDirectory, request.SourcePath);
                    }
                    catch (Exception exception)
                        when (exception is IOException or UnauthorizedAccessException)
                    {
                        _console.Warning(
                            $"Unable to remove empty source directories: {exception.Message}"
                        );
                    }
                }

                return 0;
            }

            RestoreManagedFileMoves(movedOperations, createdDirectories);
            RestoreManagedFileMoveTargets(selection.Moves);
            return _console.Fail(savedState.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreManagedFileMoves(movedOperations, createdDirectories);
            RestoreManagedFileMoveTargets(selection.Moves);
            return _console.Fail($"Unable to move managed files: {exception.Message}");
        }
    }

    private string? ValidateManagedFileMoveOperations(
        IReadOnlyList<ManagedFileMoveOperation> operations
    )
    {
        var invalidOperation = operations.FirstOrDefault(operation =>
            operation.SourceExists == fileSystem.File.Exists(operation.TargetFilePath)
        );
        if (invalidOperation is not null)
        {
            return $"Managed file move for '{invalidOperation.Move.SourcePath}' requires an existing source and missing target, or a missing source and existing target.";
        }

        return operations.Select(operation => operation.SourceExists).Distinct().Count() > 1
            ? "Managed directory move requires every file to be moved or every lock record to be rebound."
            : null;
    }

    private ManagedFileMoveOperation CreateManagedFileMoveOperation(
        string projectDirectory,
        ManagedFileMove move
    )
    {
        var sourceFilePath = fileSystem.Path.GetFullPath(move.SourcePath, projectDirectory);
        return new ManagedFileMoveOperation(
            move,
            sourceFilePath,
            fileSystem.Path.GetFullPath(move.TargetPath, projectDirectory),
            fileSystem.File.Exists(sourceFilePath)
        );
    }

    private static ManifestOperationResult<ProjectConfiguration> AddSavedMoveRemapping(
        ProjectConfiguration configuration,
        ManagedFileMoveSelection selection,
        ManagedFileMoveRequest request
    )
    {
        var remapping = configuration.Remap ?? new ProjectConfiguration.Remapping();
        var directories = new Dictionary<string, string>(
            remapping.Directories,
            StringComparer.Ordinal
        );
        var files = new Dictionary<string, string>(remapping.Files, StringComparer.Ordinal);
        if (!selection.IsDirectory)
        {
            var declaredTarget = selection.Moves[0].ManagedFile.DeclaredTargetPath;
            files[NormalizePath(declaredTarget ?? request.SourcePath)] = request.TargetPath;
        }
        else
        {
            var declaredSource = FindDeclaredMoveDirectory(selection.Moves, request.SourcePath);
            if (declaredSource is null)
            {
                return ManifestOperationResult<ProjectConfiguration>.Failure(
                    "Managed directory remapping cannot be derived from lock-file declared targets."
                );
            }

            directories[declaredSource] = request.TargetPath;
        }

        return ManifestOperationResult<ProjectConfiguration>.Success(
            configuration with
            {
                Remap = new ProjectConfiguration.Remapping
                {
                    Directories = directories,
                    Files = files,
                },
            }
        );
    }

    private static string? FindDeclaredMoveDirectory(
        IReadOnlyList<ManagedFileMove> moves,
        string sourceDirectory
    )
    {
        var sourcePrefix = $"{sourceDirectory}/";
        var declaredDirectories = moves
            .Select(move =>
            {
                var declaredTarget = NormalizePath(
                    move.ManagedFile.DeclaredTargetPath ?? string.Empty
                );
                var relativePath = move.SourcePath[sourcePrefix.Length..];
                var suffix = $"/{relativePath}";
                return declaredTarget.EndsWith(suffix, StringComparison.Ordinal)
                    ? declaredTarget[..^suffix.Length]
                    : null;
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return declaredDirectories.Count == 1 ? declaredDirectories[0] : null;
    }

    private void RestoreManagedFileMoves(
        IReadOnlyList<ManagedFileMoveOperation> movedOperations,
        IReadOnlyList<string> createdDirectories
    )
    {
        foreach (var operation in movedOperations.Reverse())
        {
            if (fileSystem.File.Exists(operation.TargetFilePath))
            {
                fileSystem.File.Move(operation.TargetFilePath, operation.SourceFilePath);
            }
        }

        foreach (var directory in createdDirectories.Distinct(StringComparer.Ordinal).Reverse())
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

    private static void RestoreManagedFileMoveTargets(IReadOnlyList<ManagedFileMove> moves)
    {
        foreach (var move in moves)
        {
            move.ManagedFile.SetTargetPath(move.SourcePath);
        }
    }

    private void RemoveEmptyMovedDirectories(string projectDirectory, string sourceDirectory)
    {
        var sourcePath = fileSystem.Path.GetFullPath(sourceDirectory, projectDirectory);
        if (!fileSystem.Directory.Exists(sourcePath))
        {
            return;
        }

        foreach (
            var directory in fileSystem
                .Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length)
        )
        {
            if (!fileSystem.Directory.EnumerateFileSystemEntries(directory).Any())
            {
                fileSystem.Directory.Delete(directory);
            }
        }

        if (!fileSystem.Directory.EnumerateFileSystemEntries(sourcePath).Any())
        {
            fileSystem.Directory.Delete(sourcePath);
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
            updateRequest,
            previewSources: true
        );
        if (preparation.Value is not { } preparedUpdate)
        {
            return _console.Fail(preparation.Error);
        }

        WriteManagedFileTemplateDiagnostics(preparedUpdate.InstallationPlan);

        await using (preparedUpdate.Materialization)
        await using (preparedUpdate.ExternalMaterialization)
        {
            var hooks = await AuthorizeHooksAsync(
                projectDirectory,
                preparedUpdate.State,
                preparedUpdate.Configuration,
                preparedUpdate.Graph,
                preparedUpdate.Parameters,
                updateRequest.ScriptMode,
                updateRequest.SkipInstructions
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

        WriteManagedFileTemplateDiagnostics(preparedUpdate.InstallationPlan);

        await using (preparedUpdate.Materialization)
        await using (preparedUpdate.ExternalMaterialization)
        {
            var lifecycle = await CreateDryRunLifecyclePlanAsync(
                projectDirectory,
                preparedUpdate.State,
                preparedUpdate.Configuration,
                preparedUpdate.Graph,
                preparedUpdate.Parameters,
                updateRequest.ScriptMode,
                updateRequest.SkipInstructions
            );
            return lifecycle.Value is { } dryRunLifecycle
                ? ManifestOperationResult<PackUpdatePlan>.Success(
                    preparedUpdate.UpdatePlan with
                    {
                        Lifecycle = dryRunLifecycle,
                        ExternalSources = preparedUpdate.ExternalSources,
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
        PackInstallationRequest installationRequest,
        bool previewSources = false
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
            Remap = installationRequest.SaveRemapping
                ? installationRequest.TargetRemapping?.MergeInto(state.Configuration.Remap)
                : state.Configuration.Remap,
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

            var externalSources = await PrepareExternalSourcesAsync(
                materialization.Graph,
                nextConfiguration,
                resolvedParameters,
                installationRequest.AcceptSources,
                previewSources
            );
            if (externalSources.Value is not { } preparedSources)
            {
                return ManifestOperationResult<PreparedPackInstallation>.Failure(
                    externalSources.Error ?? "Unable to prepare external sources."
                );
            }

            var externalMaterialization = await _externalSourceMaterializer.MaterializeAsync(
                preparedSources.Requirements
            );
            if (externalMaterialization.Value is not { } materializedSources)
            {
                return ManifestOperationResult<PreparedPackInstallation>.Failure(
                    externalMaterialization.Error ?? "Unable to materialize external sources."
                );
            }

            var retainExternalMaterialization = false;
            try
            {
                var installationPlan = installationPlanner.Plan(
                    projectDirectory,
                    materialization.Graph,
                    state.LockFile,
                    preparedSources.CandidateConfiguration,
                    updatePlanningRequest,
                    resolvedParameters,
                    materializedSources.Roots
                );
                if (installationPlan.Value is not { } plan)
                {
                    return ManifestOperationResult<PreparedPackInstallation>.Failure(
                        installationPlan.Error ?? "Unable to plan pack installation."
                    );
                }

                if (
                    ManagedRootInventory.FindCrossRootCollision(
                        ManagedRootInventory.FromInstallationPlan(materialization.Graph, plan),
                        state.LockFile
                    ) is
                    { } collision
                )
                {
                    return ManifestOperationResult<PreparedPackInstallation>.Failure(collision);
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
                retainExternalMaterialization = true;
                return ManifestOperationResult<PreparedPackInstallation>.Success(
                    new PreparedPackInstallation(
                        state,
                        preparedSources.CandidateConfiguration,
                        materialization.Graph,
                        plan,
                        plannedUpdate,
                        resolvedParameters,
                        new PackReference(selectedPack.Manifest.Id, selectedPack.Manifest.Version),
                        materialization,
                        materializedSources,
                        preparedSources.Requirements
                    )
                );
            }
            finally
            {
                if (!retainExternalMaterialization)
                {
                    await materializedSources.DisposeAsync();
                }
            }
        }
        finally
        {
            if (!retainMaterialization)
            {
                await materialization.DisposeAsync();
            }
        }
    }

    private void WriteManagedFileTemplateDiagnostics(PackInstallationPlan installationPlan)
    {
        foreach (var diagnostic in installationPlan.Diagnostics)
        {
            _console.Warning(
                $"Managed file target '{diagnostic.ReferencedDeclaredTarget}' could not be resolved while rendering '{diagnostic.CurrentEffectiveTarget}'."
            );
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
        PackInstallationRequest updateRequest,
        bool previewSources = false
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<PreparedPackUpdate>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        var driftValidation = ExternalSourceDriftValidator.Validate(state);
        if (!driftValidation.IsSuccess)
        {
            return ManifestOperationResult<PreparedPackUpdate>.Failure(
                driftValidation.Error ?? "External source configuration drift was detected."
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

            var externalSources = await PrepareExternalSourcesAsync(
                materialization.Graph,
                nextConfiguration,
                resolvedParameters,
                updateRequest.AcceptSources,
                previewSources
            );
            if (externalSources.Value is not { } preparedSources)
            {
                return ManifestOperationResult<PreparedPackUpdate>.Failure(
                    externalSources.Error ?? "Unable to prepare external sources."
                );
            }

            var externalMaterialization = await _externalSourceMaterializer.MaterializeAsync(
                preparedSources.Requirements
            );
            if (externalMaterialization.Value is not { } materializedSources)
            {
                return ManifestOperationResult<PreparedPackUpdate>.Failure(
                    externalMaterialization.Error ?? "Unable to materialize external sources."
                );
            }

            var retainExternalMaterialization = false;
            try
            {
                var installationPlan = installationPlanner.Plan(
                    projectDirectory,
                    materialization.Graph,
                    state.LockFile,
                    preparedSources.CandidateConfiguration,
                    updatePlanningRequest,
                    resolvedParameters,
                    materializedSources.Roots
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
                retainExternalMaterialization = true;
                return ManifestOperationResult<PreparedPackUpdate>.Success(
                    new PreparedPackUpdate(
                        state,
                        preparedSources.CandidateConfiguration,
                        materialization.Graph,
                        plan,
                        plannedUpdate,
                        resolvedParameters,
                        materialization,
                        materializedSources,
                        preparedSources.Requirements
                    )
                );
            }
            finally
            {
                if (!retainExternalMaterialization)
                {
                    await materializedSources.DisposeAsync();
                }
            }
        }
        finally
        {
            if (!retainMaterialization)
            {
                await materialization.DisposeAsync();
            }
        }
    }

    private async Task<
        ManifestOperationResult<ApprovedExternalSourcePlan>
    > PrepareExternalSourcesAsync(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        ResolvedPackParameters parameters,
        bool acceptSources,
        bool previewSources
    )
    {
        var requirements = await _externalSourceRequirementPlanner.PlanAsync(
            graph,
            configuration,
            parameters
        );
        return requirements.Value is { } plan
            ? previewSources
                ? ExternalSourceConsentCoordinator.Preview(plan, configuration)
                : await _externalSourceConsentCoordinator.ApproveAsync(
                    plan,
                    configuration,
                    acceptSources
                )
            : ManifestOperationResult<ApprovedExternalSourcePlan>.Failure(
                requirements.Error ?? "Unable to plan external sources."
            );
    }

    public async Task<int> UninstallAsync(
        string projectDirectory,
        PackInstallationRequest hookRequest,
        Action<TimeSpan>? onManagedFileChangesApplied = null
    )
    {
        var packReference = hookRequest.PackReference;
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

        var preparation = PrepareUninstall(state, requestedRoot, projectDirectory);
        if (preparation.Value is not { } prepared)
        {
            return _console.Fail(preparation.Error);
        }

        var exitCode = await ExecutePreparedUninstallAsync(
            projectDirectory,
            state,
            hookRequest,
            prepared,
            onManagedFileChangesApplied
        );
        if (exitCode == 0)
        {
            foreach (
                var sourceName in GetUnusedExternalSources(
                    state.Configuration,
                    prepared.RemovedPacks,
                    prepared.NextState.LockFile
                )
            )
            {
                _console.Info(
                    $"External source '{sourceName}' has no remaining consumers. Remove it with 'luna sources rm {sourceName}'."
                );
            }
        }

        return exitCode;
    }

    private ManifestOperationResult<PreparedUninstall> PrepareUninstall(
        ProjectState state,
        ProjectConfiguration.RequestedPack requestedRoot,
        string projectDirectory
    )
    {
        var nextConfiguration = state.Configuration with
        {
            Packs =
            [
                .. state.Configuration.Packs.Where(pack =>
                    !string.Equals(pack.Id, requestedRoot.Id, StringComparison.Ordinal)
                ),
            ],
        };
        var nextLockFile = CreateRemainingLockFile(nextConfiguration.Packs, state.LockFile);
        if (nextLockFile.Value is not { } lockFile)
        {
            return ManifestOperationResult<PreparedUninstall>.Failure(
                nextLockFile.Error ?? "Unable to create the remaining lock state."
            );
        }

        var removedPacks = GetRemovedPacks(state.LockFile, lockFile);
        var managedFilesToRemove = GetManagedFilesToRemove(removedPacks, lockFile);
        var changedFile = managedFilesToRemove.FirstOrDefault(managedFile =>
            ManagedTargetExists(managedFile.ManagedFile, projectDirectory)
            && !ManagedTargetIsUnchanged(managedFile.ManagedFile, projectDirectory)
        );
        if (changedFile is not null)
        {
            return ManifestOperationResult<PreparedUninstall>.Failure(
                $"Managed target '{changedFile.ManagedFile.TargetPath}' has changed."
            );
        }

        return ManifestOperationResult<PreparedUninstall>.Success(
            new PreparedUninstall(
                requestedRoot,
                state with
                {
                    Configuration = nextConfiguration,
                    LockFile = lockFile,
                },
                removedPacks,
                managedFilesToRemove
            )
        );
    }

    private static IReadOnlyList<string> GetUnusedExternalSources(
        ProjectConfiguration configuration,
        IReadOnlyList<ProjectLockFile.ResolvedPack> removedPacks,
        ProjectLockFile remainingLockFile
    )
    {
        var remainingConsumers = remainingLockFile
            .Packs.Select(pack => pack.SourceName)
            .Concat(
                remainingLockFile.Packs.SelectMany(pack =>
                    pack.ExternalSources.Values.Select(source => source.SourceName)
                )
            )
            .ToHashSet(StringComparer.Ordinal);
        var configuredGitSources = configuration
            .Sources.OfType<ProjectConfiguration.GitSource>()
            .Select(source => source.Name)
            .ToHashSet(StringComparer.Ordinal);
        return
        [
            .. removedPacks
                .SelectMany(pack => pack.ExternalSources.Values)
                .Select(source => source.SourceName)
                .Where(configuredGitSources.Contains)
                .Where(sourceName => !remainingConsumers.Contains(sourceName))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(sourceName => sourceName, StringComparer.Ordinal),
        ];
    }

    private async Task<int> ExecutePreparedUninstallAsync(
        string projectDirectory,
        ProjectState state,
        PackInstallationRequest hookRequest,
        PreparedUninstall prepared,
        Action<TimeSpan>? onManagedFileChangesApplied
    )
    {
        var materialization = await TryMaterializeUninstallGraphAsync(
            projectDirectory,
            state,
            prepared.RequestedRoot
        );
        if (materialization is null)
        {
            return await DeleteAndSaveAsync(
                state,
                prepared.NextState,
                prepared.ManagedFilesToRemove,
                projectDirectory,
                null,
                onManagedFileChangesApplied
            );
        }

        await using (materialization)
        {
            return await ExecuteMaterializedUninstallAsync(
                projectDirectory,
                state,
                hookRequest,
                prepared,
                materialization,
                onManagedFileChangesApplied
            );
        }
    }

    private async Task<int> ExecuteMaterializedUninstallAsync(
        string projectDirectory,
        ProjectState state,
        PackInstallationRequest hookRequest,
        PreparedUninstall prepared,
        GitPackMaterialization materialization,
        Action<TimeSpan>? onManagedFileChangesApplied
    )
    {
        var removedPackIds = prepared
            .RemovedPacks.Select(pack => pack.Id)
            .ToHashSet(StringComparer.Ordinal);
        var lifecyclePlan = PackLifecyclePlanner.PlanRemoval(
            materialization.Graph,
            state.LockFile,
            removedPackIds
        );
        if (!HasUninstallHooks(lifecyclePlan))
        {
            return await DeleteAndSaveAsync(
                state,
                prepared.NextState,
                prepared.ManagedFilesToRemove,
                projectDirectory,
                null,
                onManagedFileChangesApplied
            );
        }

        var parameters = ResolveUninstallHookParameters(
            materialization.Graph,
            state.Configuration,
            hookRequest,
            lifecyclePlan
        );
        if (parameters.Value is not { } resolvedParameters)
        {
            return _console.Fail(
                parameters.Error ?? "Unable to resolve uninstall hook parameters."
            );
        }

        var hooks = await AuthorizeLifecyclePlanAsync(
            projectDirectory,
            state.Configuration,
            lifecyclePlan,
            resolvedParameters,
            hookRequest.ScriptMode,
            hookRequest.SkipInstructions
        );
        if (hooks.Value is not { } authorizedHooks)
        {
            return _console.Fail(hooks.Error);
        }

        return await DeleteAndSaveAsync(
            state,
            prepared.NextState,
            prepared.ManagedFilesToRemove,
            projectDirectory,
            authorizedHooks,
            onManagedFileChangesApplied
        );
    }

    private static bool HasUninstallHooks(PackLifecyclePlan plan) =>
        plan.Changes.Any(change =>
            change.IncomingPack?.Manifest.Hooks is { } hooks
            && (hooks.PreUninstall is { Count: > 0 } || hooks.PostUninstall is { Count: > 0 })
        );

    private static ManifestOperationResult<ResolvedPackParameters> ResolveUninstallHookParameters(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        PackInstallationRequest hookRequest,
        PackLifecyclePlan plan
    )
    {
        var requiresParameters = plan.Changes.Any(change =>
            change.IncomingPack?.Manifest.Hooks is { } hooks
            && new[] { hooks.PreUninstall, hooks.PostUninstall }
                .Where(declarations => declarations is not null)
                .SelectMany(declarations => declarations!)
                .Any(hook =>
                    hook.Templating == true
                    || hook.Arguments.Any(argument =>
                        argument.Contains("{{", StringComparison.Ordinal)
                    )
                )
        );
        return requiresParameters
            ? PackParameterResolver.Resolve(graph, configuration, hookRequest)
            : ManifestOperationResult<ResolvedPackParameters>.Success(
                new ResolvedPackParameters(
                    new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal),
                    new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
                )
            );
    }

    private async Task<GitPackMaterialization?> TryMaterializeUninstallGraphAsync(
        string projectDirectory,
        ProjectState state,
        ProjectConfiguration.RequestedPack requestedRoot
    )
    {
        var installedRoot = state.LockFile.Packs.Find(pack =>
            string.Equals(pack.Id, requestedRoot.Id, StringComparison.Ordinal)
        );
        var graph = await graphResolver.ResolveAsync(
            projectDirectory,
            state.Configuration,
            requestedRoot.Id,
            installedRoot?.Version ?? requestedRoot.Version
        );
        if (graph.Value is not { } resolvedGraph)
        {
            WarnUninstallHooksUnavailable(requestedRoot.Id, graph.Error);
            return null;
        }

        var materialization = await _gitPackMaterializer.MaterializeAsync(
            resolvedGraph,
            state.Configuration
        );
        if (materialization.Value is not { } snapshot)
        {
            WarnUninstallHooksUnavailable(requestedRoot.Id, materialization.Error);
            return null;
        }

        return snapshot;
    }

    private void WarnUninstallHooksUnavailable(string packId, string? reason)
    {
        _console.Warning(
            $"Uninstall hooks for pack '{packId}' are unavailable; continuing without them. {reason ?? "The configured source could not be loaded."}"
        );
        _console.Info(string.Empty);
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

    private async Task<int> ApplyUpdateAndSaveAsync(
        ProjectState state,
        ProjectConfiguration nextConfiguration,
        ResolvedPackGraph graph,
        PackInstallationPlan installationPlan,
        PackUpdatePlan updatePlan,
        string projectDirectory,
        bool preserveExistingLock = false,
        AuthorizedLifecycleHooks? authorizedHooks = null,
        Action<TimeSpan>? onManagedFileChangesApplied = null
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

        var mutationStartedAt = Stopwatch.GetTimestamp();
        var appliedUpdate = updateTransaction.Apply(updatePlan);
        if (appliedUpdate.Value is not { } rollback)
        {
            return _console.Fail(appliedUpdate.Error);
        }

        onManagedFileChangesApplied?.Invoke(Stopwatch.GetElapsedTime(mutationStartedAt));

        var isCheckpointPersisted = false;
        var isPersisted = false;
        try
        {
            var completion = await CompleteUpdateAsync(
                state,
                nextConfiguration,
                graph,
                installationPlan,
                updatePlan,
                projectDirectory,
                preserveExistingLock,
                authorizedHooks
            );
            isCheckpointPersisted = completion.IsCheckpointPersisted;
            isPersisted = completion.IsPersisted;
            return completion.ExitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return _console.Fail($"Unable to update pack: {exception.Message}");
        }
        finally
        {
            if (!isPersisted)
            {
                if (isCheckpointPersisted)
                {
                    var restoredState = await projectStateStore.SaveAsync(projectDirectory, state);
                    if (!restoredState.IsSuccess)
                    {
                        _console.Error(
                            restoredState.Error
                                ?? "Unable to restore project state after lifecycle failure."
                        );
                    }
                }

                rollback.Restore();
            }
        }
    }

    private async Task<(
        int ExitCode,
        bool IsCheckpointPersisted,
        bool IsPersisted
    )> CompleteUpdateAsync(
        ProjectState state,
        ProjectConfiguration nextConfiguration,
        ResolvedPackGraph graph,
        PackInstallationPlan installationPlan,
        PackUpdatePlan updatePlan,
        string projectDirectory,
        bool preserveExistingLock,
        AuthorizedLifecycleHooks? authorizedHooks
    )
    {
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
            return (_console.Fail(mergedLockFile.Error), false, false);
        }

        foreach (var (targetPath, contents) in resultingContents)
        {
            UpdateManagedFileHash(nextLockFile, targetPath, contents);
        }

        var nextState = state with { Configuration = nextConfiguration, LockFile = nextLockFile };
        var checkpoint = await projectStateStore.SaveAsync(projectDirectory, nextState);
        if (!checkpoint.IsSuccess)
        {
            return (_console.Fail(checkpoint.Error), false, false);
        }

        var postExecution = await ExecuteHooksAsync(
            projectDirectory,
            authorizedHooks?.PostMutation ?? [],
            CreateManifestSnapshot(projectDirectory)
        );
        if (!postExecution.IsSuccess)
        {
            return (_console.Fail(postExecution.Error), true, false);
        }

        var savedState = await projectStateStore.SaveAsync(projectDirectory, nextState);
        return savedState.IsSuccess
            ? (0, true, true)
            : (_console.Fail(savedState.Error), true, false);
    }

    private async Task<ManifestOperationResult<AuthorizedLifecycleHooks>> AuthorizeHooksAsync(
        string projectDirectory,
        ProjectState state,
        ProjectConfiguration configuration,
        ResolvedPackGraph graph,
        ResolvedPackParameters parameters,
        ScriptExecutionMode scriptMode,
        bool skipInstructions
    )
    {
        var lifecyclePlan = PackLifecyclePlanner.Plan(graph, state.LockFile);
        return await AuthorizeLifecyclePlanAsync(
            projectDirectory,
            configuration,
            lifecyclePlan,
            parameters,
            scriptMode,
            skipInstructions
        );
    }

    private async Task<
        ManifestOperationResult<AuthorizedLifecycleHooks>
    > AuthorizeLifecyclePlanAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        PackLifecyclePlan lifecyclePlan,
        ResolvedPackParameters parameters,
        ScriptExecutionMode scriptMode,
        bool skipInstructions
    )
    {
        var preHooks = _hookPlanner.PlanPreMutation(lifecyclePlan, parameters, skipInstructions);
        var postHooks = _hookPlanner.PlanPostMutation(lifecyclePlan, parameters, skipInstructions);
        if (
            preHooks.Value is not { } plannedPreHooks
            || postHooks.Value is not { } plannedPostHooks
        )
        {
            return ManifestOperationResult<AuthorizedLifecycleHooks>.Failure(
                preHooks.Error ?? postHooks.Error ?? "Unable to plan lifecycle hooks."
            );
        }

        var authorized = await _hookAuthorizer.AuthorizeWithDiagnosticsAsync(
            projectDirectory,
            configuration,
            scriptMode,
            [.. plannedPreHooks, .. plannedPostHooks]
        );
        if (authorized.Value is not { } authorization)
        {
            return ManifestOperationResult<AuthorizedLifecycleHooks>.Failure(
                authorized.Error ?? "Unable to authorize lifecycle hooks."
            );
        }

        foreach (var deniedScript in authorization.DeniedScripts)
        {
            _console.Warning(LifecycleScriptDenialFormatter.Format(deniedScript));
        }

        var hooks = authorization.AuthorizedHooks;

        return ManifestOperationResult<AuthorizedLifecycleHooks>.Success(
            new AuthorizedLifecycleHooks(
                [
                    .. hooks.Where(hook =>
                        hook.Invocation.Hook
                            is LifecycleHook.PreInstall
                                or LifecycleHook.PreUpdate
                                or LifecycleHook.PreUninstall
                    ),
                ],
                [
                    .. hooks.Where(hook =>
                        hook.Invocation.Hook
                            is LifecycleHook.PostInstall
                                or LifecycleHook.PostUpdate
                                or LifecycleHook.PostUninstall
                    ),
                ]
            )
        );
    }

    private async Task<ManifestOperationResult<LifecycleDryRunPlan>> CreateDryRunLifecyclePlanAsync(
        string projectDirectory,
        ProjectState state,
        ProjectConfiguration configuration,
        ResolvedPackGraph graph,
        ResolvedPackParameters parameters,
        ScriptExecutionMode scriptMode,
        bool skipInstructions
    )
    {
        var lifecyclePlan = PackLifecyclePlanner.Plan(graph, state.LockFile);
        var preHooks = _hookPlanner.PlanPreMutation(lifecyclePlan, parameters, skipInstructions);
        var postHooks = _hookPlanner.PlanPostMutation(lifecyclePlan, parameters, skipInstructions);
        if (
            preHooks.Value is not { } plannedPreHooks
            || postHooks.Value is not { } plannedPostHooks
        )
        {
            return ManifestOperationResult<LifecycleDryRunPlan>.Failure(
                preHooks.Error ?? postHooks.Error ?? "Unable to plan lifecycle hooks."
            );
        }

        IReadOnlyList<ScriptDenialOrigin> denyingScopes = [];
        if (plannedPreHooks.Concat(plannedPostHooks).Any(static hook => hook.IsScript))
        {
            var policy = await _hookAuthorizer.EvaluateScriptPolicyAsync(
                projectDirectory,
                configuration
            );
            if (policy.Value is not { } evaluation)
            {
                return ManifestOperationResult<LifecycleDryRunPlan>.Failure(
                    policy.Error ?? "Unable to evaluate lifecycle script policy."
                );
            }

            denyingScopes = evaluation.DenyingScopes;
        }

        return ManifestOperationResult<LifecycleDryRunPlan>.Success(
            new LifecycleDryRunPlan(
                scriptMode,
                plannedPreHooks,
                plannedPostHooks,
                lifecyclePlan.Changes,
                denyingScopes
            )
        );
    }

    private async Task<ManifestOperationResult<bool>> ExecuteHooksAsync(
        string projectDirectory,
        IReadOnlyList<AuthorizedLifecycleHook> hooks,
        ManifestSnapshot manifestSnapshot
    )
    {
        foreach (var hook in hooks)
        {
            var execution = await DispatchHookAsync(projectDirectory, hook);
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

    private async Task<ManifestOperationResult<bool>> DispatchHookAsync(
        string projectDirectory,
        AuthorizedLifecycleHook hook
    )
    {
        if (hook.Script is { } script)
        {
            return await _hookExecutor.ExecuteAsync(projectDirectory, script);
        }

        if (hook.Invocation.Instruction is not { } instruction)
        {
            return ManifestOperationResult<bool>.Failure("Lifecycle instruction was not prepared.");
        }

        var verified = instruction.PackedFile.Verify(fileSystem);
        if (!verified.IsSuccess)
        {
            return ManifestOperationResult<bool>.Failure(
                verified.Error ?? "Packed lifecycle instruction integrity verification failed."
            );
        }

        return new InstructionPresenter(_console).Present(
            hook.Invocation.Pack.Manifest.Id,
            instruction
        );
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
                Links = CloneLinks(updatedLockFile),
                Packs =
                [
                    .. previousLockFile.Packs.Where(pack => !updatedIds.Contains(pack.Id)),
                    .. updatedLockFile.Packs,
                ],
            }
        );
    }

    private async Task<int> DeleteAndSaveAsync(
        ProjectState previousState,
        ProjectState nextState,
        IReadOnlyList<ManagedFileRemoval> managedFilesToRemove,
        string projectDirectory,
        AuthorizedLifecycleHooks? authorizedHooks,
        Action<TimeSpan>? onManagedFileChangesApplied
    )
    {
        var preExecution = await ExecuteHooksAsync(
            projectDirectory,
            authorizedHooks?.PreMutation ?? [],
            CreateManifestSnapshot(projectDirectory)
        );
        if (!preExecution.IsSuccess)
        {
            return _console.Fail(preExecution.Error);
        }

        var transaction = new UninstallTransactionState();
        var snapshots = new List<ManagedFileSnapshot>();
        try
        {
            return await ExecuteUninstallTransactionAsync(
                projectDirectory,
                nextState,
                managedFilesToRemove,
                authorizedHooks,
                snapshots,
                transaction,
                onManagedFileChangesApplied
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return _console.Fail($"Unable to uninstall pack: {exception.Message}");
        }
        finally
        {
            await RestoreFailedUninstallAsync(
                projectDirectory,
                previousState,
                snapshots,
                transaction
            );
        }
    }

    private async Task<int> ExecuteUninstallTransactionAsync(
        string projectDirectory,
        ProjectState nextState,
        IReadOnlyList<ManagedFileRemoval> managedFilesToRemove,
        AuthorizedLifecycleHooks? authorizedHooks,
        ICollection<ManagedFileSnapshot> snapshots,
        UninstallTransactionState transaction,
        Action<TimeSpan>? onManagedFileChangesApplied
    )
    {
        var mutationStartedAt = Stopwatch.GetTimestamp();
        var removal = ApplyRemovals(
            managedFilesToRemove,
            projectDirectory,
            nextState.LockFile,
            snapshots
        );
        if (!removal.IsSuccess)
        {
            return _console.Fail(removal.Error ?? "Unable to remove managed files.");
        }

        onManagedFileChangesApplied?.Invoke(Stopwatch.GetElapsedTime(mutationStartedAt));
        var checkpoint = await projectStateStore.SaveAllowingUnavailableSourcesAsync(
            projectDirectory,
            nextState
        );
        if (!checkpoint.IsSuccess)
        {
            return _console.Fail(checkpoint.Error);
        }

        transaction.IsCheckpointPersisted = true;
        var postExecution = await ExecuteHooksAsync(
            projectDirectory,
            authorizedHooks?.PostMutation ?? [],
            CreateManifestSnapshot(projectDirectory)
        );
        if (!postExecution.IsSuccess)
        {
            return _console.Fail(postExecution.Error);
        }

        var savedState = await projectStateStore.SaveAllowingUnavailableSourcesAsync(
            projectDirectory,
            nextState
        );
        if (!savedState.IsSuccess)
        {
            return _console.Fail(savedState.Error);
        }

        transaction.IsPersisted = true;
        return 0;
    }

    private ManifestOperationResult<bool> ApplyRemovals(
        IReadOnlyList<ManagedFileRemoval> removals,
        string projectDirectory,
        ProjectLockFile lockFile,
        ICollection<ManagedFileSnapshot> snapshots
    )
    {
        foreach (var removal in removals)
        {
            var appliedRemoval = ApplyRemoval(removal, projectDirectory, lockFile, snapshots);
            if (!appliedRemoval.IsSuccess)
            {
                return appliedRemoval;
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private async Task RestoreFailedUninstallAsync(
        string projectDirectory,
        ProjectState previousState,
        IReadOnlyList<ManagedFileSnapshot> snapshots,
        UninstallTransactionState transaction
    )
    {
        if (transaction.IsPersisted)
        {
            return;
        }

        if (transaction.IsCheckpointPersisted)
        {
            var restoredState = await projectStateStore.SaveAllowingUnavailableSourcesAsync(
                projectDirectory,
                previousState
            );
            if (!restoredState.IsSuccess)
            {
                _console.Error(
                    restoredState.Error
                        ?? "Unable to restore project state after uninstall hook failure."
                );
            }
        }

        RestoreManagedFiles(snapshots);
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
            var externalSources = CreateLockExternalSources(pack, installationPlan);
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
                    ExternalSources = externalSources,
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

        return new ProjectLockFile
        {
            SchemaVersion = 1,
            Links = CloneLinks(previousLockFile),
            Packs = resolvedPacks,
        };
    }

    private static Dictionary<string, ProjectLockFile.ExternalSourceLock> CreateLockExternalSources(
        DiscoveredPack pack,
        PackInstallationPlan installationPlan
    ) =>
        installationPlan
            .ManagedFiles.Where(managedFile => IsSamePack(managedFile.Pack, pack))
            .Select(managedFile => managedFile.ExternalSource)
            .OfType<PlannedExternalSource>()
            .GroupBy(source => source.Alias, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var source = group.First();
                    return new ProjectLockFile.ExternalSourceLock
                    {
                        Fingerprint = source.Fingerprint,
                        Ref = source.Ref,
                        ResolvedCommit = source.ResolvedCommit,
                        SourceName = source.SourceName,
                    };
                },
                StringComparer.Ordinal
            );

    private static Dictionary<string, ProjectLockFile.ResolvedLink> CloneLinks(
        ProjectLockFile lockFile
    ) => new(lockFile.Links, StringComparer.Ordinal);

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
            SourceAlias = managedFile.ExternalSource?.Alias,
            SourceFingerprint = managedFile.ExternalSource?.Fingerprint,
            SourceName = managedFile.ExternalSource?.SourceName,
            SourcePath = managedFile.ExternalSource?.SourcePath,
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
                Links = CloneLinks(previousLockFile),
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

    private sealed record ManagedFileMove(
        ManagedMoveFile ManagedFile,
        string SourcePath,
        string TargetPath
    );

    private sealed record ManagedMoveFile(
        string? DeclaredTargetPath,
        string TargetPath,
        Action<string> SetTargetPath
    );

    private sealed record ManagedFileMoveSelection(
        IReadOnlyList<ManagedFileMove> Moves,
        bool IsDirectory
    );

    private sealed record ManagedFileMoveOperation(
        ManagedFileMove Move,
        string SourceFilePath,
        string TargetFilePath,
        bool SourceExists
    );

    private sealed record ManagedFileSnapshot(string Path, byte[] Contents);

    private sealed record ManifestSnapshot(string Path, byte[] Contents);

    private sealed record PreparedUninstall(
        ProjectConfiguration.RequestedPack RequestedRoot,
        ProjectState NextState,
        IReadOnlyList<ProjectLockFile.ResolvedPack> RemovedPacks,
        IReadOnlyList<ManagedFileRemoval> ManagedFilesToRemove
    );

    private sealed class UninstallTransactionState
    {
        public bool IsCheckpointPersisted { get; set; }

        public bool IsPersisted { get; set; }
    }

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
