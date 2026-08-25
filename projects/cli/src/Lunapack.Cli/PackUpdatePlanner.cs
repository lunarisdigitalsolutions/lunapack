using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunapack.Cli;

internal sealed class PackUpdatePlanner(IFileSystem fileSystem)
{
    private static readonly UTF8Encoding _utf8 = new(false, true);

    public ManifestOperationResult<PackUpdatePlan> Plan(
        string projectDirectory,
        ProjectLockFile previousLockFile,
        PackInstallationPlan installationPlan,
        bool removeUnplannedManagedFiles = true
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
        if (plannedTargets.Value is not { } plannedTargetMap)
        {
            return ManifestOperationResult<PackUpdatePlan>.Failure(
                plannedTargets.Error ?? "Unable to read planned target ownership."
            );
        }

        var updateActions = CreateActions(
            projectDirectory,
            previousTargetMap,
            installationPlan.ManagedFiles,
            plannedTargetMap,
            removeUnplannedManagedFiles
        );
        if (updateActions.Value is not { } actions)
        {
            return ManifestOperationResult<PackUpdatePlan>.Failure(
                updateActions.Error ?? "Unable to plan managed-file updates."
            );
        }

        return ManifestOperationResult<PackUpdatePlan>.Success(new PackUpdatePlan(actions));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Update planning retains managed-file conflict decisions in one pass."
    )]
    private ManifestOperationResult<List<PlannedPackUpdateAction>> CreateActions(
        string projectDirectory,
        Dictionary<PackTargetKey, PreviousManagedTarget> previousTargetMap,
        IReadOnlyList<PlannedManagedFile> plannedManagedFiles,
        Dictionary<PackTargetKey, PlannedManagedFile> plannedTargetMap,
        bool removeUnplannedManagedFiles
    )
    {
        var actions = new List<PlannedPackUpdateAction>();
        var plannedResultingContents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var managedFile in plannedManagedFiles)
        {
            var key = new PackTargetKey(
                managedFile.Pack.Manifest.Id,
                NormalizePath(managedFile.DeclaredTargetPath)
            );
            previousTargetMap.TryGetValue(key, out var previousTarget);
            var effectiveManagedFile = GetEffectiveManagedFile(
                projectDirectory,
                managedFile,
                previousTarget
            );
            var targetPath = NormalizePath(effectiveManagedFile.TargetPathRelativeToProject);
            byte[]? targetContents = null;
            if (!plannedResultingContents.TryGetValue(targetPath, out targetContents))
            {
                targetContents = fileSystem.File.Exists(effectiveManagedFile.TargetPath)
                    ? fileSystem.File.ReadAllBytes(effectiveManagedFile.TargetPath)
                    : null;
            }

            if (
                previousTarget is not null
                && targetContents is not null
                && string.Equals(
                    ComputeSha256(managedFile.Contents),
                    previousTarget.ManagedFile.Sha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }

            var plannedAction = CreateUpdateAction(
                effectiveManagedFile,
                previousTarget,
                targetContents
            );
            if (plannedAction.Value is not { } action)
            {
                return ManifestOperationResult<List<PlannedPackUpdateAction>>.Failure(
                    plannedAction.Error ?? "Unable to plan managed-file update."
                );
            }

            actions.Add(action);
            if (action.ResultingContents is { } contents)
            {
                plannedResultingContents[targetPath] = contents;
            }
        }

        if (!removeUnplannedManagedFiles)
        {
            return ManifestOperationResult<List<PlannedPackUpdateAction>>.Success(actions);
        }

        var plannedTargetPaths = plannedManagedFiles
            .Select(managedFile => NormalizePath(managedFile.TargetPathRelativeToProject))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (key, previousTarget) in previousTargetMap)
        {
            if (
                plannedTargetMap.ContainsKey(key)
                || plannedTargetPaths.Contains(NormalizePath(previousTarget.ManagedFile.TargetPath))
            )
            {
                continue;
            }

            actions.Add(
                new DeleteManagedFileUpdateAction(
                    previousTarget.Pack,
                    previousTarget.ManagedFile,
                    fileSystem.Path.GetFullPath(
                        previousTarget.ManagedFile.TargetPath,
                        projectDirectory
                    )
                )
            );
        }

        return ManifestOperationResult<List<PlannedPackUpdateAction>>.Success(actions);
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
        PreviousManagedTarget? previousTarget
    ) =>
        previousTarget is null
            ? managedFile
            : managedFile with
            {
                TargetPath = fileSystem.Path.GetFullPath(
                    previousTarget.ManagedFile.TargetPath,
                    projectDirectory
                ),
                TargetPathRelativeToProject = NormalizePath(previousTarget.ManagedFile.TargetPath),
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

            if (
                firstMarkerIndexes.Count != 1
                || lastMarkerIndexes.Count != 1
                || firstMarkerIndexes[0] >= lastMarkerIndexes[0]
            )
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
                _utf8.GetBytes(merged.ToJsonString(LunapackJsonSerializerOptions.Default))
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
                    NormalizePath(managedFile.DeclaredTargetPath ?? managedFile.TargetPath)
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
                NormalizePath(managedFile.DeclaredTargetPath)
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

    private static string NormalizePath(string path) => ProjectPath.Normalize(path);

    private sealed record PackTargetKey(string PackId, string TargetPath);

    private sealed record PreviousManagedTarget(
        ProjectLockFile.ResolvedPack Pack,
        ProjectLockFile.ManagedFile ManagedFile
    );
}
