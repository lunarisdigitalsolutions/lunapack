using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class PackUpdatePlanner(IFileSystem fileSystem)
{
    private static readonly UTF8Encoding _utf8 = new(false, true);
    private static readonly JsonSerializerOptions _mergedJsonOptions = new(
        LunapackJsonSerializerOptions.Default
    )
    {
        WriteIndented = true,
    };

    public ManifestOperationResult<PackUpdatePlan> Plan(
        string projectDirectory,
        ProjectLockFile previousLockFile,
        PackInstallationPlan installationPlan,
        bool removeUnplannedManagedFiles = true,
        bool removeModified = false
    )
    {
        var previousTargets = CreatePreviousTargetMap(previousLockFile);
        if (previousTargets.Value is not { } previousTargetMap)
        {
            return ManifestOperationResult<PackUpdatePlan>.Failure(
                previousTargets.Error ?? "Unable to read lock-file ownership."
            );
        }

        var plannedTargets = CreatePlannedTargetMap(installationPlan);
        if (plannedTargets.Value is null)
        {
            return ManifestOperationResult<PackUpdatePlan>.Failure(
                plannedTargets.Error ?? "Unable to read planned target ownership."
            );
        }

        var commandRemappedTargets = installationPlan
            .Remappings.Where(remapping => remapping.Origin == ManagedFileRemappingOrigin.Command)
            .Select(remapping => new PackTargetKey(
                remapping.PackId,
                ProjectPath.Normalize(remapping.DeclaredTarget)
            ))
            .ToHashSet();

        var updateActions = CreateActions(
            projectDirectory,
            previousTargetMap,
            installationPlan.ManagedFiles,
            installationPlan.IgnoredDeclaredTargets,
            commandRemappedTargets,
            removeUnplannedManagedFiles,
            removeModified
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
            ProjectPath.Normalize(remapping.DeclaredTarget)
        ));
        foreach (var managedFile in installationPlan.ManagedFiles)
        {
            var declaredTarget = ProjectPath.Normalize(managedFile.DeclaredTargetPath);
            var key = new PackTargetKey(managedFile.Pack.Manifest.Id, declaredTarget);
            if (
                remappings.TryGetValue(key, out var configuredRemapping)
                && configuredRemapping.Origin == ManagedFileRemappingOrigin.Command
            )
            {
                continue;
            }

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
        IReadOnlySet<string> ignoredDeclaredTargets,
        IReadOnlySet<PackTargetKey> commandRemappedTargets,
        bool removeUnplannedManagedFiles,
        bool removeModified
    )
    {
        var actions = new List<PlannedPackUpdateAction>();
        var plannedResultingContents = new Dictionary<string, byte[]>(
            ProjectPath.ProjectRelativePathComparer(fileSystem)
        );

        var plannedUpdates = PlanManagedFileUpdates(
            projectDirectory,
            previousTargetMap,
            plannedManagedFiles,
            actions,
            plannedResultingContents,
            commandRemappedTargets
        );
        if (!plannedUpdates.IsSuccess)
        {
            return ManifestOperationResult<List<PlannedPackUpdateAction>>.Failure(
                plannedUpdates.Error ?? "Unable to plan managed-file update."
            );
        }

        if (removeUnplannedManagedFiles)
        {
            var plannedRemovals = PlanManagedFileRemovals(
                projectDirectory,
                previousTargetMap,
                plannedManagedFiles,
                ignoredDeclaredTargets,
                commandRemappedTargets,
                actions,
                removeModified
            );
            if (!plannedRemovals.IsSuccess)
            {
                return ManifestOperationResult<List<PlannedPackUpdateAction>>.Failure(
                    plannedRemovals.Error ?? "Unable to plan managed-file removals."
                );
            }
        }

        return ManifestOperationResult<List<PlannedPackUpdateAction>>.Success(actions);
    }

    private ManifestOperationResult<bool> PlanManagedFileUpdates(
        string projectDirectory,
        Dictionary<PackTargetKey, PreviousManagedTarget> previousTargetMap,
        IReadOnlyList<PlannedManagedFile> plannedManagedFiles,
        List<PlannedPackUpdateAction> actions,
        Dictionary<string, byte[]> plannedResultingContents,
        IReadOnlySet<PackTargetKey> commandRemappedTargets
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
                commandRemappedTargets
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
        IReadOnlySet<PackTargetKey> commandRemappedTargets
    )
    {
        var key = new PackTargetKey(
            managedFile.Pack.Manifest.Id,
            ProjectPath.Normalize(managedFile.DeclaredTargetPath)
        );
        previousTargetMap.TryGetValue(key, out var previousTarget);
        var effectiveManagedFile = GetEffectiveManagedFile(
            projectDirectory,
            managedFile,
            previousTarget,
            commandRemappedTargets.Contains(key)
        );
        var normalizedTargetPath = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            effectiveManagedFile.TargetPathRelativeToProject
        );
        if (normalizedTargetPath.Value is not { } targetPath)
        {
            return ManifestOperationResult<bool>.Failure(
                normalizedTargetPath.Error
                    ?? $"Invalid managed target '{effectiveManagedFile.TargetPathRelativeToProject}'."
            );
        }

        if (!plannedResultingContents.TryGetValue(targetPath, out var targetContents))
        {
            targetContents = fileSystem.File.Exists(effectiveManagedFile.TargetPath)
                ? fileSystem.File.ReadAllBytes(effectiveManagedFile.TargetPath)
                : null;
        }

        var relocation = TargetWasRelocated(projectDirectory, previousTarget, targetPath);
        if (!relocation.IsSuccess)
        {
            return ManifestOperationResult<bool>.Failure(
                relocation.Error ?? "Unable to compare managed target paths."
            );
        }
        var targetWasRelocated = relocation.Value;

        var managedFileDoesNotRequireUpdate =
            previousTarget is not null
            && !targetWasRelocated
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

    private ManifestOperationResult<bool> TargetWasRelocated(
        string projectDirectory,
        PreviousManagedTarget? previousTarget,
        string targetPath
    )
    {
        if (previousTarget is null)
        {
            return ManifestOperationResult<bool>.Success(false);
        }

        var previousTargetPath = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            previousTarget.ManagedFile.TargetPath
        );
        return previousTargetPath.Value is { } normalizedPreviousTargetPath
            ? ManifestOperationResult<bool>.Success(
                !ProjectPath
                    .ProjectRelativePathComparer(fileSystem)
                    .Equals(normalizedPreviousTargetPath, targetPath)
            )
            : ManifestOperationResult<bool>.Failure(
                previousTargetPath.Error
                    ?? $"Invalid locked managed target '{previousTarget.ManagedFile.TargetPath}'."
            );
    }

    private ManifestOperationResult<bool> PlanManagedFileRemovals(
        string projectDirectory,
        Dictionary<PackTargetKey, PreviousManagedTarget> previousTargetMap,
        IReadOnlyList<PlannedManagedFile> plannedManagedFiles,
        IReadOnlySet<string> ignoredDeclaredTargets,
        IReadOnlySet<PackTargetKey> commandRemappedTargets,
        List<PlannedPackUpdateAction> actions,
        bool removeModified
    )
    {
        var plannedTargets = CreatePlannedTargetPathMap(
            projectDirectory,
            previousTargetMap,
            plannedManagedFiles,
            commandRemappedTargets
        );
        if (plannedTargets.Value is not { } plannedEffectiveTargets)
        {
            return ManifestOperationResult<bool>.Failure(
                plannedTargets.Error ?? "Unable to normalize planned managed targets."
            );
        }

        var previousTargets = CreatePreviousTargetPathGroups(projectDirectory, previousTargetMap);
        if (previousTargets.Value is not { } previousTargetsByPath)
        {
            return ManifestOperationResult<bool>.Failure(
                previousTargets.Error ?? "Unable to normalize previous managed targets."
            );
        }

        var plannedTargetPaths = plannedEffectiveTargets.Values.ToHashSet(
            ProjectPath.ProjectRelativePathComparer(fileSystem)
        );
        foreach (
            var previousTargetsForPath in previousTargetsByPath.OrderBy(
                entry => entry.Key,
                StringComparer.Ordinal
            )
        )
        {
            if (
                previousTargetsForPath.Value.Any(entry =>
                    TargetIsStillPlannedOrIgnored(
                        entry.Key,
                        previousTargetsForPath.Key,
                        plannedEffectiveTargets,
                        plannedTargetPaths,
                        ignoredDeclaredTargets,
                        commandRemappedTargets
                    )
                )
            )
            {
                continue;
            }

            var plannedRemoval = PlanManagedFileRemoval(
                projectDirectory,
                previousTargetsForPath.Key,
                previousTargetsForPath.Value,
                actions,
                removeModified
            );
            if (!plannedRemoval.IsSuccess)
            {
                return ManifestOperationResult<bool>.Failure(
                    plannedRemoval.Error ?? "Unable to plan managed-file removal."
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private ManifestOperationResult<bool> PlanManagedFileRemoval(
        string projectDirectory,
        string previousTargetPath,
        IReadOnlyList<KeyValuePair<PackTargetKey, PreviousManagedTarget>> previousTargets,
        List<PlannedPackUpdateAction> actions,
        bool removeModified
    )
    {
        var previousTarget = previousTargets[0].Value;
        var targetPath = fileSystem.Path.GetFullPath(previousTargetPath, projectDirectory);
        try
        {
            ProjectMutationPathSecurity.EnsureNoAliases(fileSystem, projectDirectory, targetPath);
            if (!fileSystem.File.Exists(targetPath))
            {
                return ManifestOperationResult<bool>.Success(true);
            }

            var plannedTargetSha256 = ComputeSha256(fileSystem.File.ReadAllBytes(targetPath));
            var wasModified = previousTargets.Any(entry =>
                !string.Equals(
                    entry.Value.ManagedFile.Sha256,
                    plannedTargetSha256,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            var previousOwner = new ManagedRootOwner(
                ManagedRootKind.Pack,
                previousTarget.Pack.Id,
                previousTarget.Pack.Version
            );
            var previousFile = new ManagedRootFile(
                previousTarget.Pack.PackPath,
                previousTarget.ManagedFile.DeclaredTargetPath
                    ?? previousTarget.ManagedFile.TargetPath,
                previousTargetPath,
                previousTarget.ManagedFile.Sha256
            );
            if (wasModified && !removeModified)
            {
                actions.Add(
                    new PreserveManagedFileUpdateAction(previousOwner, previousFile, targetPath)
                );
                return ManifestOperationResult<bool>.Success(true);
            }

            actions.Add(
                new DeleteManagedFileUpdateAction(
                    previousOwner,
                    previousFile,
                    targetPath,
                    plannedTargetSha256,
                    wasModified
                )
            );
            return ManifestOperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Unable to inspect obsolete managed target '{previousTargetPath}': {exception.Message}"
            );
        }
    }

    private static bool TargetIsStillPlannedOrIgnored(
        PackTargetKey key,
        string previousTargetPath,
        IReadOnlyDictionary<PackTargetKey, string> plannedEffectiveTargets,
        HashSet<string> plannedTargetPaths,
        IReadOnlySet<string> ignoredDeclaredTargets,
        IReadOnlySet<PackTargetKey> commandRemappedTargets
    )
    {
        var plannedTargetRetainsPreviousPath =
            plannedEffectiveTargets.TryGetValue(key, out var plannedTargetPath)
            && (
                !commandRemappedTargets.Contains(key)
                || plannedTargetPaths.Comparer.Equals(plannedTargetPath, previousTargetPath)
            );
        return plannedTargetRetainsPreviousPath
            || plannedTargetPaths.Contains(previousTargetPath)
            || ignoredDeclaredTargets.Contains(key.TargetPath);
    }

    private ManifestOperationResult<Dictionary<PackTargetKey, string>> CreatePlannedTargetPathMap(
        string projectDirectory,
        IReadOnlyDictionary<PackTargetKey, PreviousManagedTarget> previousTargets,
        IReadOnlyList<PlannedManagedFile> plannedManagedFiles,
        IReadOnlySet<PackTargetKey> commandRemappedTargets
    )
    {
        var targets = new Dictionary<PackTargetKey, string>();
        foreach (var managedFile in plannedManagedFiles)
        {
            var key = new PackTargetKey(
                managedFile.Pack.Manifest.Id,
                ProjectPath.Normalize(managedFile.DeclaredTargetPath)
            );
            previousTargets.TryGetValue(key, out var previousTarget);
            var effectiveManagedFile = GetEffectiveManagedFile(
                projectDirectory,
                managedFile,
                previousTarget,
                commandRemappedTargets.Contains(key)
            );
            var normalized = ProjectPath.NormalizeProjectRelativePath(
                fileSystem,
                projectDirectory,
                effectiveManagedFile.TargetPathRelativeToProject
            );
            if (normalized.Value is not { } targetPath)
            {
                return ManifestOperationResult<Dictionary<PackTargetKey, string>>.Failure(
                    normalized.Error
                        ?? $"Invalid managed target '{managedFile.TargetPathRelativeToProject}'."
                );
            }

            targets.Add(key, targetPath);
        }

        return ManifestOperationResult<Dictionary<PackTargetKey, string>>.Success(targets);
    }

    private ManifestOperationResult<
        Dictionary<string, List<KeyValuePair<PackTargetKey, PreviousManagedTarget>>>
    > CreatePreviousTargetPathGroups(
        string projectDirectory,
        IReadOnlyDictionary<PackTargetKey, PreviousManagedTarget> previousTargets
    )
    {
        var groups = new Dictionary<
            string,
            List<KeyValuePair<PackTargetKey, PreviousManagedTarget>>
        >(ProjectPath.ProjectRelativePathComparer(fileSystem));
        foreach (var entry in previousTargets)
        {
            var normalized = ProjectPath.NormalizeProjectRelativePath(
                fileSystem,
                projectDirectory,
                entry.Value.ManagedFile.TargetPath
            );
            if (normalized.Value is not { } targetPath)
            {
                return ManifestOperationResult<
                    Dictionary<string, List<KeyValuePair<PackTargetKey, PreviousManagedTarget>>>
                >.Failure(
                    normalized.Error
                        ?? $"Invalid locked managed target '{entry.Value.ManagedFile.TargetPath}'."
                );
            }

            if (!groups.TryGetValue(targetPath, out var group))
            {
                group = [];
                groups.Add(targetPath, group);
            }

            group.Add(entry);
        }

        return ManifestOperationResult<
            Dictionary<string, List<KeyValuePair<PackTargetKey, PreviousManagedTarget>>>
        >.Success(groups);
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
            "copy" => CreateCopyAction(managedFile, previousTarget, targetContents),
            "merge" => CreateMergeAction(managedFile, previousTarget, targetContents),
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

    private ManifestOperationResult<PlannedPackUpdateAction> CreateCopyAction(
        PlannedManagedFile managedFile,
        PreviousManagedTarget? previousTarget,
        byte[] targetContents
    )
    {
        if (!string.Equals(managedFile.Strategy.Type, "copy", StringComparison.Ordinal))
        {
            return ManifestOperationResult<PlannedPackUpdateAction>.Failure(
                $"Managed target '{managedFile.TargetPathRelativeToProject}' uses unsupported strategy '{managedFile.Strategy.Type}/{managedFile.Strategy.Method}'."
            );
        }

        return managedFile.Strategy.Method switch
        {
            "overwrite" => ManifestOperationResult<PlannedPackUpdateAction>.Success(
                new CopyManagedFileUpdateAction(managedFile, previousTarget?.ManagedFile)
            ),
            "fail-if-exists" => ManifestOperationResult<PlannedPackUpdateAction>.Failure(
                $"Managed target '{managedFile.TargetPathRelativeToProject}' already exists."
            ),
            "skip-if-exists" => ManifestOperationResult<PlannedPackUpdateAction>.Success(
                new SkipManagedFileUpdateAction(
                    managedFile,
                    previousTarget?.ManagedFile,
                    targetContents
                )
            ),
            "backup-and-overwrite" => ManifestOperationResult<PlannedPackUpdateAction>.Success(
                new BackupAndCopyManagedFileUpdateAction(
                    managedFile,
                    previousTarget?.ManagedFile,
                    CreateBackupPath(managedFile.TargetPath)
                )
            ),
            _ => ManifestOperationResult<PlannedPackUpdateAction>.Failure(
                $"Managed target '{managedFile.TargetPathRelativeToProject}' uses unsupported copy method '{managedFile.Strategy.Method}'."
            ),
        };
    }

    private static ManifestOperationResult<PlannedPackUpdateAction> CreateMergeAction(
        PlannedManagedFile managedFile,
        PreviousManagedTarget? previousTarget,
        byte[] targetContents
    )
    {
        var mergedContents = managedFile.Strategy.Method switch
        {
            "lines" => MergeLines(targetContents, managedFile.Contents),
            "section" => MergeSection(targetContents, managedFile.Contents),
            "json" => MergeJson(targetContents, managedFile.Contents),
            _ => ManifestOperationResult<byte[]>.Failure(
                $"Managed target '{managedFile.TargetPathRelativeToProject}' uses unsupported merge method '{managedFile.Strategy.Method}'."
            ),
        };
        if (mergedContents.Value is not { } contents)
        {
            return ManifestOperationResult<PlannedPackUpdateAction>.Failure(
                mergedContents.Error ?? "Unable to merge managed file."
            );
        }

        return ManifestOperationResult<PlannedPackUpdateAction>.Success(
            managedFile.Strategy.Method switch
            {
                "lines" => new MergeLinesManagedFileUpdateAction(
                    managedFile,
                    previousTarget?.ManagedFile,
                    contents
                ),
                "section" => new MergeSectionManagedFileUpdateAction(
                    managedFile,
                    previousTarget?.ManagedFile,
                    contents
                ),
                "json" => new MergeJsonManagedFileUpdateAction(
                    managedFile,
                    previousTarget?.ManagedFile,
                    contents
                ),
                _ => throw new InvalidOperationException("Unsupported merge method."),
            }
        );
    }

    private string CreateBackupPath(string targetPath)
    {
        var suffix = 1;
        var backupPath = $"{targetPath}.{suffix}";
        while (fileSystem.File.Exists(backupPath))
        {
            suffix++;
            backupPath = $"{targetPath}.{suffix}";
        }

        return backupPath;
    }

    private static ManifestOperationResult<byte[]> MergeLines(
        byte[] targetContents,
        byte[] sourceContents
    )
    {
        try
        {
            var targetText = GetUtf8Text(targetContents);
            var sourceText = GetUtf8Text(sourceContents);
            var targetLines = ReadLines(targetText);
            var sourceLines = ReadLines(sourceText);
            var knownLines = new HashSet<string>(targetLines, StringComparer.Ordinal);
            foreach (var sourceLine in sourceLines)
            {
                if (knownLines.Add(sourceLine))
                {
                    targetLines.Add(sourceLine);
                }
            }

            return ManifestOperationResult<byte[]>.Success(
                CreateTextContents(
                    targetLines,
                    HasTrailingNewline(targetText) || HasTrailingNewline(sourceText)
                )
            );
        }
        catch (DecoderFallbackException exception)
        {
            return ManifestOperationResult<byte[]>.Failure(
                $"Line merge requires UTF-8 text: {exception.Message}"
            );
        }
    }

    private static ManifestOperationResult<byte[]> MergeSection(
        byte[] targetContents,
        byte[] sourceContents
    )
    {
        try
        {
            var targetText = GetUtf8Text(targetContents);
            var sourceText = GetUtf8Text(sourceContents);
            var sourceLines = ReadLines(sourceText);
            if (sourceLines.Count < 2)
            {
                return ManifestOperationResult<byte[]>.Failure(
                    "Section merge requires distinct first and last source marker lines."
                );
            }

            var targetLines = ReadLines(targetText);
            var firstMarkerIndexes = FindMarkerIndexes(targetLines, sourceLines[0]);
            var lastMarkerIndexes = FindMarkerIndexes(targetLines, sourceLines[^1]);
            if (firstMarkerIndexes.Count == 0 && lastMarkerIndexes.Count == 0)
            {
                targetLines.AddRange(sourceLines);
                return ManifestOperationResult<byte[]>.Success(
                    CreateTextContents(
                        targetLines,
                        HasTrailingNewline(targetText) || HasTrailingNewline(sourceText)
                    )
                );
            }

            var markersAreIncompleteOrAmbiguous =
                firstMarkerIndexes.Count != 1
                || lastMarkerIndexes.Count != 1
                || firstMarkerIndexes[0] >= lastMarkerIndexes[0];
            if (markersAreIncompleteOrAmbiguous)
            {
                return ManifestOperationResult<byte[]>.Failure(
                    "Section merge markers are incomplete or ambiguous."
                );
            }

            var firstMarkerIndex = firstMarkerIndexes[0];
            targetLines.RemoveRange(firstMarkerIndex, lastMarkerIndexes[0] - firstMarkerIndex + 1);
            targetLines.InsertRange(firstMarkerIndex, sourceLines);
            return ManifestOperationResult<byte[]>.Success(
                CreateTextContents(
                    targetLines,
                    HasTrailingNewline(targetText) || HasTrailingNewline(sourceText)
                )
            );
        }
        catch (DecoderFallbackException exception)
        {
            return ManifestOperationResult<byte[]>.Failure(
                $"Section merge requires UTF-8 text: {exception.Message}"
            );
        }
    }

    private static ManifestOperationResult<byte[]> MergeJson(
        byte[] targetContents,
        byte[] sourceContents
    )
    {
        try
        {
            var target = JsonNode.Parse(GetUtf8Text(targetContents));
            var source = JsonNode.Parse(GetUtf8Text(sourceContents));
            JsonNode? merged = (target, source) switch
            {
                (JsonObject targetObject, JsonObject sourceObject) => MergeJsonObjects(
                    targetObject,
                    sourceObject
                ),
                (JsonArray targetArray, JsonArray sourceArray) => MergeJsonArrays(
                    targetArray,
                    sourceArray
                ),
                _ => null,
            };
            if (merged is null)
            {
                return ManifestOperationResult<byte[]>.Failure(
                    "JSON merge requires source and target JSON objects or arrays of the same kind."
                );
            }

            return ManifestOperationResult<byte[]>.Success(
                _utf8.GetBytes(merged.ToJsonString(_mergedJsonOptions))
            );
        }
        catch (Exception exception) when (exception is DecoderFallbackException or JsonException)
        {
            return ManifestOperationResult<byte[]>.Failure(
                $"JSON merge requires valid UTF-8 JSON: {exception.Message}"
            );
        }
    }

    private static JsonObject MergeJsonObjects(JsonObject target, JsonObject source)
    {
        foreach (var (key, sourceValue) in source)
        {
            target.TryGetPropertyValue(key, out var targetValue);
            target[key] = MergeJsonValues(targetValue, sourceValue);
        }

        return target;
    }

    private static JsonArray MergeJsonArrays(JsonArray target, JsonArray source)
    {
        foreach (var sourceValue in source)
        {
            if (!target.Any(targetValue => JsonNode.DeepEquals(targetValue, sourceValue)))
            {
                target.Add(sourceValue?.DeepClone());
            }
        }

        return target;
    }

    private static JsonNode? MergeJsonValues(JsonNode? targetValue, JsonNode? sourceValue) =>
        (targetValue, sourceValue) switch
        {
            (JsonObject targetObject, JsonObject sourceObject) => MergeJsonObjects(
                targetObject,
                sourceObject
            ),
            (JsonArray targetArray, JsonArray sourceArray) => MergeJsonArrays(
                targetArray,
                sourceArray
            ),
            _ => sourceValue?.DeepClone(),
        };

    private static List<int> FindMarkerIndexes(IReadOnlyList<string> lines, string marker) =>
        [
            .. lines
                .Select((line, index) => (line, index))
                .Where(item => string.Equals(item.line, marker, StringComparison.Ordinal))
                .Select(item => item.index),
        ];

    private static string GetUtf8Text(byte[] contents) => _utf8.GetString(contents);

    private static List<string> ReadLines(string contents)
    {
        var normalized = contents
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static bool HasTrailingNewline(string contents) =>
        contents.EndsWith('\n') || contents.EndsWith('\r');

    private static byte[] CreateTextContents(IReadOnlyList<string> lines, bool trailingNewline)
    {
        var contents = string.Join("\n", lines);
        if (trailingNewline && contents.Length > 0)
        {
            contents += '\n';
        }

        return _utf8.GetBytes(contents);
    }

    private static ManifestOperationResult<
        Dictionary<PackTargetKey, PreviousManagedTarget>
    > CreatePreviousTargetMap(ProjectLockFile lockFile)
    {
        var targets = new Dictionary<PackTargetKey, PreviousManagedTarget>();
        foreach (var pack in lockFile.Packs)
        {
            foreach (var managedFile in pack.ManagedFiles)
            {
                var key = new PackTargetKey(
                    pack.Id,
                    ProjectPath.Normalize(managedFile.DeclaredTargetPath ?? managedFile.TargetPath)
                );
                if (!targets.TryAdd(key, new PreviousManagedTarget(pack, managedFile)))
                {
                    return ManifestOperationResult<
                        Dictionary<PackTargetKey, PreviousManagedTarget>
                    >.Failure(
                        $"Lock file assigns target '{managedFile.TargetPath}' more than once for pack '{pack.Id}'."
                    );
                }
            }
        }

        return ManifestOperationResult<Dictionary<PackTargetKey, PreviousManagedTarget>>.Success(
            targets
        );
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

    private sealed record PackTargetKey(string PackId, string TargetPath);

    private sealed record PreviousManagedTarget(
        ProjectLockFile.ResolvedPack Pack,
        ProjectLockFile.ManagedFile ManagedFile
    );
}
