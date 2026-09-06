using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class PackUpdatePlanner(IFileSystem fileSystem)
{
    private readonly CopyManagedFileUpdatePlanner _copyPlanner = new(fileSystem);

    public ManifestOperationResult<PackUpdatePlan> Plan(
        string projectDirectory,
        ProjectLockFile previousLockFile,
        PackInstallationPlan installationPlan,
        bool removeUnplannedManagedFiles = true
    )
    {
        var previousTargets = CreatePreviousTargetMap(
            previousLockFile,
            installationPlan.RootIdentity
        );
        if (previousTargets.Value is not { } previousTargetMap)
        {
            return ManifestOperationResult<PackUpdatePlan>.Failure(
                previousTargets.Error ?? "Unable to read lock-file ownership."
            );
        }

        var plannedTargets = CreatePlannedTargetMap(installationPlan);
        if (plannedTargets.Value is not { } plannedTargetMap)
        {
            return ManifestOperationResult<PackUpdatePlan>.Failure(
                plannedTargets.Error ?? "Unable to read planned target ownership."
            );
        }

        var commandRemappedTargets = installationPlan
            .Remappings.Where(remapping => remapping.Origin == ManagedFileRemappingOrigin.Command)
            .Select(remapping => new PackTargetKey(
                remapping.PackId,
                GetAlias(remapping.PackId, installationPlan.RootIdentity),
                ProjectPath.Normalize(remapping.DeclaredTarget)
            ))
            .ToHashSet();

        var updateActions = CreateActions(
            projectDirectory,
            previousTargetMap,
            installationPlan.ManagedFiles,
            plannedTargetMap,
            installationPlan.IgnoredDeclaredTargets,
            commandRemappedTargets,
            installationPlan.RootIdentity,
            removeUnplannedManagedFiles
        );
        if (updateActions.Value is not { } actions)
        {
            return ManifestOperationResult<PackUpdatePlan>.Failure(
                updateActions.Error ?? "Unable to plan managed-file updates."
            );
        }

        return ManifestOperationResult<PackUpdatePlan>.Success(
            new PackUpdatePlan(actions)
            {
                Remappings = CreateEffectiveRemappings(previousTargetMap, installationPlan),
            }
        );
    }

    private static IReadOnlyList<ManagedFileRemapping> CreateEffectiveRemappings(
        IReadOnlyDictionary<PackTargetKey, PreviousManagedTarget> previousTargets,
        PackInstallationPlan installationPlan
    )
    {
        var remappings = installationPlan.Remappings.ToDictionary(remapping => new PackTargetKey(
            remapping.PackId,
            GetAlias(remapping.PackId, installationPlan.RootIdentity),
            ProjectPath.Normalize(remapping.DeclaredTarget)
        ));
        foreach (var managedFile in installationPlan.ManagedFiles)
        {
            var declaredTarget = ProjectPath.Normalize(managedFile.DeclaredTargetPath);
            var key = new PackTargetKey(
                managedFile.Pack.Manifest.Id,
                GetAlias(managedFile, installationPlan.RootIdentity),
                declaredTarget
            );
            if (!previousTargets.TryGetValue(key, out var previousTarget))
            {
                continue;
            }

            var lockedTarget = ProjectPath.Normalize(previousTarget.ManagedFile.TargetPath);
            if (string.Equals(declaredTarget, lockedTarget, StringComparison.Ordinal))
            {
                remappings.Remove(key);
                continue;
            }

            remappings[key] = new ManagedFileRemapping(
                managedFile.Pack.Manifest.Id,
                declaredTarget,
                lockedTarget,
                ManagedFileRemappingOrigin.Lock
            );
        }

        return
        [
            .. remappings
                .Values.OrderBy(remapping => remapping.PackId, StringComparer.Ordinal)
                .ThenBy(remapping => remapping.DeclaredTarget, StringComparer.Ordinal),
        ];
    }

    private ManifestOperationResult<List<PlannedPackUpdateAction>> CreateActions(
        string projectDirectory,
        Dictionary<PackTargetKey, PreviousManagedTarget> previousTargetMap,
        IReadOnlyList<PlannedManagedFile> plannedManagedFiles,
        Dictionary<PackTargetKey, PlannedManagedFile> plannedTargetMap,
        IReadOnlySet<string> ignoredDeclaredTargets,
        IReadOnlySet<PackTargetKey> commandRemappedTargets,
        PackInstanceIdentity? rootIdentity,
        bool removeUnplannedManagedFiles
    )
    {
        var actions = new List<PlannedPackUpdateAction>();
        var plannedResultingContents = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        var plannedUpdates = PlanManagedFileUpdates(
            projectDirectory,
            previousTargetMap,
            plannedManagedFiles,
            actions,
            plannedResultingContents,
            commandRemappedTargets,
            rootIdentity
        );
        if (!plannedUpdates.IsSuccess)
        {
            return ManifestOperationResult<List<PlannedPackUpdateAction>>.Failure(
                plannedUpdates.Error ?? "Unable to plan managed-file update."
            );
        }

        if (removeUnplannedManagedFiles)
        {
            PlanManagedFileRemovals(
                projectDirectory,
                previousTargetMap,
                plannedManagedFiles,
                plannedTargetMap,
                ignoredDeclaredTargets,
                commandRemappedTargets,
                actions
            );
        }

        return ManifestOperationResult<List<PlannedPackUpdateAction>>.Success(actions);
    }

    private ManifestOperationResult<bool> PlanManagedFileUpdates(
        string projectDirectory,
        Dictionary<PackTargetKey, PreviousManagedTarget> previousTargetMap,
        IReadOnlyList<PlannedManagedFile> plannedManagedFiles,
        List<PlannedPackUpdateAction> actions,
        Dictionary<string, byte[]> plannedResultingContents,
        IReadOnlySet<PackTargetKey> commandRemappedTargets,
        PackInstanceIdentity? rootIdentity
    )
    {
        foreach (var managedFile in plannedManagedFiles)
        {
            var plannedUpdate = PlanManagedFileUpdate(
                projectDirectory,
                previousTargetMap,
                managedFile,
                actions,
                plannedResultingContents,
                commandRemappedTargets,
                rootIdentity
            );
            if (!plannedUpdate.IsSuccess)
            {
                return ManifestOperationResult<bool>.Failure(
                    plannedUpdate.Error ?? "Unable to plan managed-file update."
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private ManifestOperationResult<bool> PlanManagedFileUpdate(
        string projectDirectory,
        Dictionary<PackTargetKey, PreviousManagedTarget> previousTargetMap,
        PlannedManagedFile managedFile,
        List<PlannedPackUpdateAction> actions,
        Dictionary<string, byte[]> plannedResultingContents,
        IReadOnlySet<PackTargetKey> commandRemappedTargets,
        PackInstanceIdentity? rootIdentity
    )
    {
        var key = new PackTargetKey(
            managedFile.Pack.Manifest.Id,
            GetAlias(managedFile, rootIdentity),
            ProjectPath.Normalize(managedFile.DeclaredTargetPath)
        );
        previousTargetMap.TryGetValue(key, out var previousTarget);
        var effectiveManagedFile = GetEffectiveManagedFile(
            projectDirectory,
            managedFile,
            previousTarget,
            commandRemappedTargets.Contains(key)
        );
        var targetPath = ProjectPath.Normalize(effectiveManagedFile.TargetPathRelativeToProject);
        if (!plannedResultingContents.TryGetValue(targetPath, out var targetContents))
        {
            targetContents = fileSystem.File.Exists(effectiveManagedFile.TargetPath)
                ? fileSystem.File.ReadAllBytes(effectiveManagedFile.TargetPath)
                : null;
        }

        var managedFileDoesNotRequireUpdate =
            previousTarget is not null
            && targetContents is not null
            && string.Equals(
                ComputeSha256(managedFile.Contents),
                previousTarget.ManagedFile.Sha256,
                StringComparison.OrdinalIgnoreCase
            );
        if (managedFileDoesNotRequireUpdate)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        var plannedAction = CreateUpdateAction(
            effectiveManagedFile,
            previousTarget,
            targetContents
        );
        if (plannedAction.Value is not { } action)
        {
            return ManifestOperationResult<bool>.Failure(
                plannedAction.Error ?? "Unable to plan managed-file update."
            );
        }

        actions.Add(action);
        if (action.ResultingContents is { } contents)
        {
            plannedResultingContents[targetPath] = contents;
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private void PlanManagedFileRemovals(
        string projectDirectory,
        Dictionary<PackTargetKey, PreviousManagedTarget> previousTargetMap,
        IReadOnlyList<PlannedManagedFile> plannedManagedFiles,
        Dictionary<PackTargetKey, PlannedManagedFile> plannedTargetMap,
        IReadOnlySet<string> ignoredDeclaredTargets,
        IReadOnlySet<PackTargetKey> commandRemappedTargets,
        List<PlannedPackUpdateAction> actions
    )
    {
        var plannedTargetPaths = plannedManagedFiles
            .Select(managedFile => ProjectPath.Normalize(managedFile.TargetPathRelativeToProject))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (key, previousTarget) in previousTargetMap)
        {
            var plannedTargetRetainsPreviousPath =
                plannedTargetMap.TryGetValue(key, out var plannedTarget)
                && (
                    !commandRemappedTargets.Contains(key)
                    || string.Equals(
                        ProjectPath.Normalize(plannedTarget.TargetPathRelativeToProject),
                        ProjectPath.Normalize(previousTarget.ManagedFile.TargetPath),
                        StringComparison.Ordinal
                    )
                );
            var targetIsStillPlannedOrIgnored =
                plannedTargetRetainsPreviousPath
                || plannedTargetPaths.Contains(
                    ProjectPath.Normalize(previousTarget.ManagedFile.TargetPath)
                )
                || ignoredDeclaredTargets.Contains(key.TargetPath);
            if (targetIsStillPlannedOrIgnored)
            {
                continue;
            }

            actions.Add(
                new DeleteManagedFileUpdateAction(
                    new ManagedRootOwner(
                        key.Alias is null ? ManagedRootKind.Pack : ManagedRootKind.PackInstance,
                        previousTarget.Pack.Id,
                        previousTarget.Pack.Version,
                        key.Alias
                    ),
                    new ManagedRootFile(
                        previousTarget.Pack.PackPath,
                        previousTarget.ManagedFile.DeclaredTargetPath
                            ?? previousTarget.ManagedFile.TargetPath,
                        previousTarget.ManagedFile.TargetPath,
                        previousTarget.ManagedFile.Sha256
                    ),
                    fileSystem.Path.GetFullPath(
                        previousTarget.ManagedFile.TargetPath,
                        projectDirectory
                    )
                )
            );
        }
    }

    private ManifestOperationResult<PlannedPackUpdateAction> CreateUpdateAction(
        PlannedManagedFile managedFile,
        PreviousManagedTarget? previousTarget,
        byte[]? targetContents
    )
    {
        if (targetContents is null)
        {
            return ManifestOperationResult<PlannedPackUpdateAction>.Success(
                new CreateManagedFileUpdateAction(managedFile)
            );
        }

        return managedFile.Strategy.Type switch
        {
            "copy" => _copyPlanner.Plan(managedFile, previousTarget?.ManagedFile, targetContents),
            "merge" => MergeManagedFileUpdatePlanner.Plan(
                managedFile,
                previousTarget?.ManagedFile,
                targetContents
            ),
            _ => ManifestOperationResult<PlannedPackUpdateAction>.Failure(
                $"Managed target '{managedFile.TargetPathRelativeToProject}' uses unsupported strategy '{managedFile.Strategy.Type}/{managedFile.Strategy.Method}'."
            ),
        };
    }

    private PlannedManagedFile GetEffectiveManagedFile(
        string projectDirectory,
        PlannedManagedFile managedFile,
        PreviousManagedTarget? previousTarget,
        bool commandRemapped
    ) =>
        previousTarget is null || commandRemapped
            ? managedFile
            : managedFile with
            {
                TargetPath = fileSystem.Path.GetFullPath(
                    previousTarget.ManagedFile.TargetPath,
                    projectDirectory
                ),
                TargetPathRelativeToProject = ProjectPath.Normalize(
                    previousTarget.ManagedFile.TargetPath
                ),
            };

    private static ManifestOperationResult<
        Dictionary<PackTargetKey, PreviousManagedTarget>
    > CreatePreviousTargetMap(ProjectLockFile lockFile, PackInstanceIdentity? rootIdentity)
    {
        var targets = new Dictionary<PackTargetKey, PreviousManagedTarget>();
        if (rootIdentity is not null)
        {
            var instance = lockFile.Instances.Find(candidate =>
                string.Equals(candidate.Id, rootIdentity.PackId, StringComparison.Ordinal)
                && string.Equals(candidate.Name, rootIdentity.Alias, StringComparison.Ordinal)
            );
            var rootPack = instance is null
                ? null
                : lockFile.Packs.Find(pack => pack.Key == instance.RootResolution);
            if (instance is not null && rootPack is not null)
            {
                var error = AddPreviousTargets(
                    targets,
                    rootPack,
                    instance.ManagedFiles,
                    rootIdentity.Alias
                );
                if (error is not null)
                {
                    return ManifestOperationResult<
                        Dictionary<PackTargetKey, PreviousManagedTarget>
                    >.Failure(error);
                }
            }
        }

        var instanceRootKeys = lockFile
            .Instances.Select(instance => instance.RootResolution)
            .ToHashSet();
        foreach (
            var pack in lockFile.Packs.Where(pack =>
                rootIdentity is null || pack.Key is null || !instanceRootKeys.Contains(pack.Key)
            )
        )
        {
            var error = AddPreviousTargets(targets, pack, pack.ManagedFiles, alias: null);
            if (error is not null)
            {
                return ManifestOperationResult<
                    Dictionary<PackTargetKey, PreviousManagedTarget>
                >.Failure(error);
            }
        }

        return ManifestOperationResult<Dictionary<PackTargetKey, PreviousManagedTarget>>.Success(
            targets
        );
    }

    private static string? AddPreviousTargets(
        Dictionary<PackTargetKey, PreviousManagedTarget> targets,
        ProjectLockFile.ResolvedPack pack,
        IEnumerable<ProjectLockFile.ManagedFile> managedFiles,
        string? alias
    )
    {
        foreach (var managedFile in managedFiles)
        {
            var key = new PackTargetKey(
                pack.Id,
                alias,
                ProjectPath.Normalize(managedFile.DeclaredTargetPath ?? managedFile.TargetPath)
            );
            if (!targets.TryAdd(key, new PreviousManagedTarget(pack, managedFile)))
            {
                return $"Lock file assigns target '{managedFile.TargetPath}' more than once for pack '{pack.Id}'.";
            }
        }

        return null;
    }

    private static ManifestOperationResult<
        Dictionary<PackTargetKey, PlannedManagedFile>
    > CreatePlannedTargetMap(PackInstallationPlan installationPlan)
    {
        var targets = new Dictionary<PackTargetKey, PlannedManagedFile>();
        foreach (var managedFile in installationPlan.ManagedFiles)
        {
            var key = new PackTargetKey(
                managedFile.Pack.Manifest.Id,
                GetAlias(managedFile, installationPlan.RootIdentity),
                ProjectPath.Normalize(managedFile.DeclaredTargetPath)
            );
            if (!targets.TryAdd(key, managedFile))
            {
                return ManifestOperationResult<
                    Dictionary<PackTargetKey, PlannedManagedFile>
                >.Failure(
                    $"Update plan assigns target '{managedFile.TargetPathRelativeToProject}' more than once for pack '{managedFile.Pack.Manifest.Id}'."
                );
            }
        }

        return ManifestOperationResult<Dictionary<PackTargetKey, PlannedManagedFile>>.Success(
            targets
        );
    }

    private static string ComputeSha256(byte[] contents) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(contents));

    private static string? GetAlias(string packId, PackInstanceIdentity? rootIdentity) =>
        rootIdentity is not null
        && string.Equals(packId, rootIdentity.PackId, StringComparison.Ordinal)
            ? rootIdentity.Alias
            : null;

    private static string? GetAlias(
        PlannedManagedFile managedFile,
        PackInstanceIdentity? rootIdentity
    ) =>
        managedFile.InstanceIdentity?.Alias ?? GetAlias(managedFile.Pack.Manifest.Id, rootIdentity);

    private sealed record PackTargetKey(string PackId, string? Alias, string TargetPath);

    private sealed record PreviousManagedTarget(
        ProjectLockFile.ResolvedPack Pack,
        ProjectLockFile.ManagedFile ManagedFile
    );
}
