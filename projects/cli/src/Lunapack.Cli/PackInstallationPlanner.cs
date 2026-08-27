using System.IO.Abstractions;
using System.Security.Cryptography;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Lunapack.Cli;

internal sealed class PackInstallationPlanner(
    IFileSystem fileSystem,
    PackTemplateRenderer templateRenderer,
    ManagedFileConditionParser conditionParser
)
{
    public ManifestOperationResult<PackInstallationPlan> Plan(
        string projectDirectory,
        ResolvedPackGraph graph,
        ProjectLockFile lockFile,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters
    )
    {
        var existingManagedTargets = CreateExistingManagedTargetMap(lockFile);
        if (existingManagedTargets.Value is not { } managedTargetMap)
        {
            return ManifestOperationResult<PackInstallationPlan>.Failure(
                existingManagedTargets.Error ?? "Unable to read lock-file ownership."
            );
        }

        var plannedManagedFiles = PlanManagedFiles(
            projectDirectory,
            graph,
            managedTargetMap,
            configuration,
            configuration.Packs,
            installationRequest,
            parameters
        );
        if (plannedManagedFiles.Value is not { } managedFiles)
        {
            return ManifestOperationResult<PackInstallationPlan>.Failure(
                plannedManagedFiles.Error ?? "Unable to plan managed files."
            );
        }

        return ManifestOperationResult<PackInstallationPlan>.Success(
            new PackInstallationPlan(managedFiles)
        );
    }

    private static ManifestOperationResult<
        Dictionary<string, List<ManagedRootOwner>>
    > CreateExistingManagedTargetMap(ProjectLockFile lockFile) =>
        ManifestOperationResult<Dictionary<string, List<ManagedRootOwner>>>.Success(
            ManagedRootInventory.CreateOwnershipMap(lockFile)
        );

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Planning iterates the resolved graph and preserves failure context at each boundary."
    )]
    private ManifestOperationResult<List<PlannedManagedFile>> PlanManagedFiles(
        string projectDirectory,
        ResolvedPackGraph graph,
        Dictionary<string, List<ManagedRootOwner>> existingManagedTargets,
        ProjectConfiguration configuration,
        IReadOnlyList<ProjectConfiguration.RequestedPack> requestedPacks,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters
    )
    {
        var plannedTargets = new Dictionary<string, List<PlannedManagedFile>>(
            StringComparer.Ordinal
        );

        foreach (var pack in graph.Packs)
        {
            foreach (var managedFile in pack.Manifest.ManagedFiles)
            {
                var selected = ShouldSelectManagedFile(managedFile, parameters);
                if (!selected.IsSuccess)
                {
                    return ManifestOperationResult<List<PlannedManagedFile>>.Failure(
                        selected.Error ?? "Unable to evaluate managed-file condition."
                    );
                }

                if (!selected.Value)
                {
                    continue;
                }

                var effectiveManagedFile = managedFile with
                {
                    Target = GetEffectiveTarget(
                        pack,
                        managedFile.Target,
                        requestedPacks,
                        configuration,
                        installationRequest
                    ),
                };
                var plannedManagedFiles = CreateManagedFilePlans(
                    projectDirectory,
                    pack,
                    effectiveManagedFile,
                    managedFile.Target,
                    existingManagedTargets,
                    installationRequest,
                    parameters
                );
                if (plannedManagedFiles.Value is not { } managedFilePlans)
                {
                    return ManifestOperationResult<List<PlannedManagedFile>>.Failure(
                        plannedManagedFiles.Error ?? "Unable to plan managed files."
                    );
                }

                foreach (var managedFilePlan in managedFilePlans)
                {
                    if (
                        plannedTargets.TryGetValue(
                            managedFilePlan.TargetPathRelativeToProject,
                            out var existingTargets
                        )
                    )
                    {
                        if (!CanShareTarget(existingTargets, managedFilePlan))
                        {
                            return ManifestOperationResult<List<PlannedManagedFile>>.Failure(
                                $"Target '{managedFilePlan.TargetPathRelativeToProject}' is claimed by both '{existingTargets[0].Pack.Manifest.Id}' and '{pack.Manifest.Id}'."
                            );
                        }

                        existingTargets.Add(managedFilePlan);
                        continue;
                    }

                    plannedTargets.Add(
                        managedFilePlan.TargetPathRelativeToProject,
                        [managedFilePlan]
                    );
                }
            }
        }

        return ManifestOperationResult<List<PlannedManagedFile>>.Success(
            plannedTargets.Values.SelectMany(managedFiles => managedFiles).ToList()
        );
    }

    private static bool CanShareTarget(
        IReadOnlyList<PlannedManagedFile> existingTargets,
        PlannedManagedFile candidate
    ) =>
        IsMergeStrategy(candidate.Strategy)
        && existingTargets.All(existingTarget =>
            IsMergeStrategy(existingTarget.Strategy)
            && !string.Equals(
                existingTarget.Pack.Manifest.Id,
                candidate.Pack.Manifest.Id,
                StringComparison.Ordinal
            )
        );

    private ManifestOperationResult<bool> ShouldSelectManagedFile(
        PackManifest.PackManagedFile managedFile,
        ResolvedPackParameters parameters
    )
    {
        if (managedFile.Condition is null)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        var parsedCondition = conditionParser.Parse(managedFile.Condition, parameters.Declarations);
        return parsedCondition.Value is { } condition
            ? ManifestOperationResult<bool>.Success(condition.Evaluate(parameters.Values))
            : ManifestOperationResult<bool>.Failure(
                parsedCondition.Error ?? "Unable to parse managed-file condition."
            );
    }

    private string GetEffectiveTarget(
        DiscoveredPack pack,
        string target,
        IReadOnlyList<ProjectConfiguration.RequestedPack> requestedPacks,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest
    )
    {
        var globalRemapping = ManagedFileTargetRemapping.FromConfiguration(configuration.Remap);
        var remappedTarget =
            installationRequest.TargetRemapping?.Resolve(target, globalRemapping)
            ?? globalRemapping.Resolve(target);
        if (!string.Equals(remappedTarget, NormalizePath(target), StringComparison.Ordinal))
        {
            return remappedTarget;
        }

        var destination = requestedPacks
            .FirstOrDefault(request =>
                string.Equals(request.Id, pack.Manifest.Id, StringComparison.Ordinal)
            )
            ?.Destination;

        return destination is null ? target : fileSystem.Path.Combine(destination, target);
    }

    private ManifestOperationResult<List<PlannedManagedFile>> CreateManagedFilePlans(
        string projectDirectory,
        DiscoveredPack pack,
        PackManifest.PackManagedFile managedFile,
        string declaredTarget,
        Dictionary<string, List<ManagedRootOwner>> existingManagedTargets,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters
    )
    {
        if (managedFile.Source is { } source)
        {
            return CreateSourceManagedFilePlan(
                projectDirectory,
                pack,
                source,
                managedFile,
                declaredTarget,
                existingManagedTargets,
                installationRequest,
                parameters
            );
        }

        if (managedFile.Directory is { } directory)
        {
            return CreateDirectoryManagedFilePlans(
                projectDirectory,
                pack,
                directory,
                managedFile.Target,
                declaredTarget,
                managedFile.Strategy,
                managedFile.Template,
                existingManagedTargets,
                installationRequest,
                parameters
            );
        }

        return managedFile.Glob is { } glob
            ? CreateGlobManagedFilePlans(
                projectDirectory,
                pack,
                glob,
                managedFile.Target,
                declaredTarget,
                managedFile.Strategy,
                managedFile.Template,
                existingManagedTargets,
                installationRequest,
                parameters
            )
            : ManifestOperationResult<List<PlannedManagedFile>>.Failure(
                $"Pack '{pack.Manifest.Id}' managed-file mapping has no selector."
            );
    }

    private ManifestOperationResult<List<PlannedManagedFile>> CreateSourceManagedFilePlan(
        string projectDirectory,
        DiscoveredPack pack,
        string source,
        PackManifest.PackManagedFile managedFile,
        string declaredTarget,
        Dictionary<string, List<ManagedRootOwner>> existingManagedTargets,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters
    )
    {
        var sourcePath = fileSystem.Path.Combine(pack.PackDirectory, source);
        if (!fileSystem.File.Exists(sourcePath))
        {
            return ManifestOperationResult<List<PlannedManagedFile>>.Failure(
                $"Pack '{pack.Manifest.Id}' source file '{source}' is unavailable."
            );
        }

        var managedFilePlan = CreateManagedFilePlan(
            projectDirectory,
            pack,
            sourcePath,
            managedFile.Target,
            declaredTarget,
            managedFile.Strategy,
            managedFile.Template,
            existingManagedTargets,
            installationRequest,
            parameters
        );
        return managedFilePlan.Value is { } plan
            ? ManifestOperationResult<List<PlannedManagedFile>>.Success([plan])
            : ManifestOperationResult<List<PlannedManagedFile>>.Failure(
                managedFilePlan.Error ?? "Unable to plan managed file."
            );
    }

    private ManifestOperationResult<List<PlannedManagedFile>> CreateDirectoryManagedFilePlans(
        string projectDirectory,
        DiscoveredPack pack,
        string directory,
        string targetDirectory,
        string declaredTargetDirectory,
        PackManifest.PackManagedFileStrategy strategy,
        bool isTemplate,
        Dictionary<string, List<ManagedRootOwner>> existingManagedTargets,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters
    )
    {
        var sourceDirectory = fileSystem.Path.Combine(pack.PackDirectory, directory);
        if (!fileSystem.Directory.Exists(sourceDirectory))
        {
            return ManifestOperationResult<List<PlannedManagedFile>>.Failure(
                $"Pack '{pack.Manifest.Id}' source directory '{directory}' is unavailable."
            );
        }

        var sourceFiles = fileSystem
            .Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new SourceFile(path, string.Empty))
            .ToList();
        return sourceFiles.Count == 0
            ? ManifestOperationResult<List<PlannedManagedFile>>.Failure(
                $"Pack '{pack.Manifest.Id}' source directory '{directory}' contains no files."
            )
            : CreateManagedFilePlans(
                projectDirectory,
                pack,
                targetDirectory,
                declaredTargetDirectory,
                sourceDirectory,
                sourceFiles,
                strategy,
                isTemplate,
                existingManagedTargets,
                installationRequest,
                parameters
            );
    }

    private ManifestOperationResult<List<PlannedManagedFile>> CreateGlobManagedFilePlans(
        string projectDirectory,
        DiscoveredPack pack,
        string glob,
        string targetDirectory,
        string declaredTargetDirectory,
        PackManifest.PackManagedFileStrategy strategy,
        bool isTemplate,
        Dictionary<string, List<ManagedRootOwner>> existingManagedTargets,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters
    )
    {
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(NormalizePath(glob));
        var sourcePaths = fileSystem
            .Directory.EnumerateFiles(pack.PackDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new SourceFile(
                path,
                NormalizePath(fileSystem.Path.GetRelativePath(pack.PackDirectory, path))
            ))
            .ToList();
        var sourcePathsByRelativePath = sourcePaths.ToDictionary(
            sourcePath => sourcePath.RelativePath,
            StringComparer.Ordinal
        );
        var matchedSourcePaths = matcher
            .Match(sourcePathsByRelativePath.Keys)
            .Files.Select(match => sourcePathsByRelativePath[NormalizePath(match.Path)])
            .OrderBy(sourcePath => sourcePath.RelativePath, StringComparer.Ordinal)
            .ToList();

        return matchedSourcePaths.Count == 0
            ? ManifestOperationResult<List<PlannedManagedFile>>.Failure(
                $"Pack '{pack.Manifest.Id}' glob '{glob}' matches no files."
            )
            : CreateManagedFilePlans(
                projectDirectory,
                pack,
                targetDirectory,
                declaredTargetDirectory,
                GetGlobBaseDirectory(pack.PackDirectory, glob),
                matchedSourcePaths,
                strategy,
                isTemplate,
                existingManagedTargets,
                installationRequest,
                parameters
            );
    }

    private ManifestOperationResult<List<PlannedManagedFile>> CreateManagedFilePlans(
        string projectDirectory,
        DiscoveredPack pack,
        string targetDirectory,
        string declaredTargetDirectory,
        string sourceDirectory,
        IReadOnlyList<SourceFile> sourceFiles,
        PackManifest.PackManagedFileStrategy strategy,
        bool isTemplate,
        Dictionary<string, List<ManagedRootOwner>> existingManagedTargets,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters
    )
    {
        var managedFiles = new List<PlannedManagedFile>(sourceFiles.Count);
        foreach (var sourceFile in sourceFiles)
        {
            var sourcePathRelativeToDirectory = fileSystem.Path.GetRelativePath(
                sourceDirectory,
                sourceFile.Path
            );
            var targetPath = fileSystem.Path.Combine(
                targetDirectory,
                sourcePathRelativeToDirectory
            );
            var declaredTargetPath = fileSystem.Path.Combine(
                declaredTargetDirectory,
                sourcePathRelativeToDirectory
            );
            var managedFilePlan = CreateManagedFilePlan(
                projectDirectory,
                pack,
                sourceFile.Path,
                targetPath,
                declaredTargetPath,
                strategy,
                isTemplate,
                existingManagedTargets,
                installationRequest,
                parameters
            );
            if (managedFilePlan.Value is not { } plan)
            {
                return ManifestOperationResult<List<PlannedManagedFile>>.Failure(
                    managedFilePlan.Error ?? "Unable to plan managed file."
                );
            }

            managedFiles.Add(plan);
        }

        return ManifestOperationResult<List<PlannedManagedFile>>.Success(managedFiles);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Plan generation validates each candidate file before returning a transactional plan."
    )]
    private ManifestOperationResult<PlannedManagedFile> CreateManagedFilePlan(
        string projectDirectory,
        DiscoveredPack pack,
        string sourcePath,
        string target,
        string declaredTarget,
        PackManifest.PackManagedFileStrategy strategy,
        bool isTemplate,
        Dictionary<string, List<ManagedRootOwner>> existingManagedTargets,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters
    )
    {
        var renderedContent = templateRenderer.Render(sourcePath, isTemplate, parameters);
        if (renderedContent.Value is not { } content)
        {
            return ManifestOperationResult<PlannedManagedFile>.Failure(
                renderedContent.Error ?? "Unable to render managed file."
            );
        }

        var targetPath = fileSystem.Path.GetFullPath(target, projectDirectory);
        var targetPathRelativeToProject = NormalizePath(
            fileSystem.Path.GetRelativePath(projectDirectory, targetPath)
        );
        if (fileSystem.File.Exists(targetPath))
        {
            if (
                existingManagedTargets.TryGetValue(
                    targetPathRelativeToProject,
                    out var existingManagedPacks
                )
            )
            {
                var ownerMatchesPack = existingManagedPacks.Any(owner =>
                    owner.Kind == ManagedRootKind.Pack
                    && string.Equals(owner.Name, pack.Manifest.Id, StringComparison.Ordinal)
                    && (
                        installationRequest.PlanningMode == PackManagedFilePlanningMode.Update
                        || string.Equals(
                            owner.Version,
                            pack.Manifest.Version,
                            StringComparison.Ordinal
                        )
                    )
                );
                var claimedByDifferentRoot = existingManagedPacks.Any(owner =>
                    owner.Kind != ManagedRootKind.Pack
                    || !string.Equals(owner.Name, pack.Manifest.Id, StringComparison.Ordinal)
                );
                if ((!ownerMatchesPack || claimedByDifferentRoot) && !IsMergeStrategy(strategy))
                {
                    return ManifestOperationResult<PlannedManagedFile>.Failure(
                        $"Target '{target}' is already managed by '{existingManagedPacks[0].Name}'."
                    );
                }
            }
            else if (!installationRequest.AdoptExisting && !IsMergeStrategy(strategy))
            {
                return ManifestOperationResult<PlannedManagedFile>.Failure(
                    $"Target '{target}' already exists and is not managed by LunaPack."
                );
            }
            else if (
                !IsMergeStrategy(strategy) && !RenderedContentMatchesTarget(content, targetPath)
            )
            {
                return ManifestOperationResult<PlannedManagedFile>.Failure(
                    $"Target '{target}' differs from the pack content and cannot be adopted."
                );
            }
        }

        return ManifestOperationResult<PlannedManagedFile>.Success(
            new PlannedManagedFile(
                pack,
                sourcePath,
                NormalizePath(declaredTarget),
                content,
                targetPath,
                targetPathRelativeToProject,
                strategy
            )
        );
    }

    private bool RenderedContentMatchesTarget(byte[] renderedContent, string targetPath) =>
        CryptographicOperations.FixedTimeEquals(
            renderedContent,
            fileSystem.File.ReadAllBytes(targetPath)
        );

    private string GetGlobBaseDirectory(string packDirectory, string glob)
    {
        var baseDirectory = packDirectory;
        foreach (
            var segment in NormalizePath(glob).Split('/', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            if (segment.IndexOfAny(['*', '?', '[']) >= 0)
            {
                break;
            }

            baseDirectory = fileSystem.Path.Combine(baseDirectory, segment);
        }

        return baseDirectory;
    }

    private static string NormalizePath(string path) => ProjectPath.Normalize(path);

    private static bool IsMergeStrategy(PackManifest.PackManagedFileStrategy strategy) =>
        string.Equals(strategy.Type, "merge", StringComparison.Ordinal);

    private sealed record SourceFile(string Path, string RelativePath);
}
