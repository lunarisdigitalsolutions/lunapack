using System.IO.Abstractions;
using System.Security.Cryptography;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class PackInstallationPlanner(
    IFileSystem fileSystem,
    PackTemplateRenderer templateRenderer
)
{
    public ManifestOperationResult<PackInstallationPlan> Plan(
        string projectDirectory,
        ResolvedPackGraph graph,
        ProjectLockFile lockFile,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters,
        ExternalContentRoots? externalContentRoots = null
    )
    {
        var ignoredDeclaredTargets = new HashSet<string>(StringComparer.Ordinal);
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
            parameters,
            externalContentRoots ?? ExternalContentRoots.Empty,
            ignoredDeclaredTargets
        );
        if (plannedManagedFiles.Value is not { } plan)
        {
            return ManifestOperationResult<PackInstallationPlan>.Failure(
                plannedManagedFiles.Error ?? "Unable to plan managed files."
            );
        }

        return ManifestOperationResult<PackInstallationPlan>.Success(
            plan with
            {
                IgnoredDeclaredTargets = ignoredDeclaredTargets,
            }
        );
    }

    private static ManifestOperationResult<
        Dictionary<string, List<ManagedRootOwner>>
    > CreateExistingManagedTargetMap(ProjectLockFile lockFile) =>
        ManifestOperationResult<Dictionary<string, List<ManagedRootOwner>>>.Success(
            ManagedRootInventory.CreateOwnershipMap(lockFile)
        );

    private ManifestOperationResult<PackInstallationPlan> PlanManagedFiles(
        string projectDirectory,
        ResolvedPackGraph graph,
        Dictionary<string, List<ManagedRootOwner>> existingManagedTargets,
        ProjectConfiguration configuration,
        IReadOnlyList<ProjectConfiguration.RequestedPack> requestedPacks,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters,
        ExternalContentRoots externalContentRoots,
        HashSet<string> ignoredDeclaredTargets
    )
    {
        var expandedCandidates = CreateManagedFileCandidates(
            graph,
            configuration,
            requestedPacks,
            installationRequest,
            parameters,
            externalContentRoots
        );
        if (expandedCandidates.Value is not { } candidates)
        {
            return ManifestOperationResult<PackInstallationPlan>.Failure(
                expandedCandidates.Error ?? "Unable to expand managed files."
            );
        }
        var remappings = candidates
            .Select(candidate => candidate.Remapping)
            .OfType<ManagedFileRemapping>()
            .Distinct()
            .ToList();
        candidates = FilterIgnoredCandidates(candidates, ignoredDeclaredTargets);
        var effectiveTargets = CreateEffectiveTargetMap(candidates);
        var diagnostics = new List<ManagedFileTemplateDiagnostic>();
        var plannedTargets = new Dictionary<string, List<PlannedManagedFile>>(
            StringComparer.Ordinal
        );
        foreach (var candidate in candidates)
        {
            var managedFilePlan = CreateManagedFilePlan(
                projectDirectory,
                candidate.Pack,
                candidate.ContentRoot,
                candidate.SourcePath,
                candidate.Target,
                candidate.DeclaredTarget,
                candidate.Strategy,
                candidate.IsTemplate,
                existingManagedTargets,
                installationRequest,
                parameters,
                new ManagedFileTemplateContext(candidate.Target, effectiveTargets),
                diagnostics
            );
            if (managedFilePlan.Value is not { } plan)
            {
                return ManifestOperationResult<PackInstallationPlan>.Failure(
                    managedFilePlan.Error ?? "Unable to plan managed file."
                );
            }

            if (
                plannedTargets.TryGetValue(
                    plan.TargetPathRelativeToProject,
                    out var existingTargets
                )
            )
            {
                if (!CanShareTarget(existingTargets, plan))
                {
                    return ManifestOperationResult<PackInstallationPlan>.Failure(
                        $"Target '{plan.TargetPathRelativeToProject}' is claimed by both '{existingTargets[0].Pack.Manifest.Id}' and '{candidate.Pack.Manifest.Id}'."
                    );
                }

                existingTargets.Add(plan);
                continue;
            }

            plannedTargets.Add(plan.TargetPathRelativeToProject, [plan]);
        }

        return ManifestOperationResult<PackInstallationPlan>.Success(
            new PackInstallationPlan(
                plannedTargets.Values.SelectMany(managedFiles => managedFiles).ToList()
            )
            {
                Diagnostics = diagnostics,
                Remappings = remappings,
            }
        );
    }

    private static List<ManagedFilePlanCandidate> FilterIgnoredCandidates(
        IReadOnlyList<ManagedFilePlanCandidate> candidates,
        HashSet<string> ignoredDeclaredTargets
    ) =>
        [
            .. candidates.Where(candidate =>
            {
                if (
                    !string.Equals(
                        candidate.Target,
                        ManagedFileTargetRemapping.IgnoreTarget,
                        StringComparison.Ordinal
                    )
                )
                {
                    return true;
                }

                ignoredDeclaredTargets.Add(ProjectPath.Normalize(candidate.DeclaredTarget));
                return false;
            }),
        ];

    private static Dictionary<string, string> CreateEffectiveTargetMap(
        IReadOnlyList<ManagedFilePlanCandidate> candidates
    ) =>
        candidates
            .GroupBy(
                candidate => ProjectPath.Normalize(candidate.DeclaredTarget),
                StringComparer.Ordinal
            )
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => ProjectPath.Normalize(group.Single().Target),
                StringComparer.Ordinal
            );

    private ManifestOperationResult<List<ManagedFilePlanCandidate>> CreateManagedFileCandidates(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        IReadOnlyList<ProjectConfiguration.RequestedPack> requestedPacks,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters,
        ExternalContentRoots externalContentRoots
    )
    {
        var candidates = new List<ManagedFilePlanCandidate>();
        foreach (var pack in graph.Packs)
        {
            foreach (var managedFile in pack.Manifest.ManagedFiles)
            {
                var selected = ShouldSelectManagedFile(managedFile, parameters);
                if (!selected.IsSuccess)
                {
                    return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                        selected.Error ?? "Unable to evaluate managed-file condition."
                    );
                }

                if (!selected.Value)
                {
                    continue;
                }

                var createdCandidates = CreateManagedFileCandidates(
                    pack,
                    managedFile,
                    managedFile.Target,
                    externalContentRoots
                );
                if (createdCandidates.Value is not { } managedFileCandidates)
                {
                    return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                        createdCandidates.Error ?? "Unable to expand managed files."
                    );
                }

                candidates.AddRange(
                    managedFileCandidates.Select(candidate =>
                    {
                        var resolution = GetEffectiveTarget(
                            pack,
                            candidate.DeclaredTarget,
                            requestedPacks,
                            configuration,
                            installationRequest
                        );
                        return candidate with
                        {
                            Target = resolution.EffectiveTarget,
                            Remapping = resolution.Remapping,
                        };
                    })
                );
            }
        }

        return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Success(candidates);
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

    private static ManifestOperationResult<bool> ShouldSelectManagedFile(
        PackManifest.PackManagedFile managedFile,
        ResolvedPackParameters parameters
    )
    {
        if (managedFile.Condition is null)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        var parsedCondition = ManagedFileConditionParser.Parse(
            managedFile.Condition,
            parameters.Declarations
        );
        return parsedCondition.Value is { } condition
            ? ManifestOperationResult<bool>.Success(condition.Evaluate(parameters.Values))
            : ManifestOperationResult<bool>.Failure(
                parsedCondition.Error ?? "Unable to parse managed-file condition."
            );
    }

    private ManagedFileTargetResolution GetEffectiveTarget(
        DiscoveredPack pack,
        string target,
        IReadOnlyList<ProjectConfiguration.RequestedPack> requestedPacks,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest
    )
    {
        var globalRemapping = ManagedFileTargetRemapping.FromConfiguration(configuration.Remap);
        var packRemapping = ManagedFileTargetRemapping.FromConfiguration(
            requestedPacks
                .FirstOrDefault(request =>
                    string.Equals(request.Id, pack.Manifest.Id, StringComparison.Ordinal)
                )
                ?.Remap
        );
        var remappedTarget = installationRequest.TargetRemapping?.TryResolve(target);
        if (remappedTarget is not null)
        {
            return CreateRemappedTargetResolution(
                pack.Manifest.Id,
                target,
                remappedTarget,
                ManagedFileRemappingOrigin.Command
            );
        }

        remappedTarget = packRemapping.TryResolve(target);
        if (remappedTarget is not null)
        {
            return CreateRemappedTargetResolution(
                pack.Manifest.Id,
                target,
                remappedTarget,
                ManagedFileRemappingOrigin.Pack
            );
        }

        remappedTarget = globalRemapping.TryResolve(target);
        if (remappedTarget is not null)
        {
            return CreateRemappedTargetResolution(
                pack.Manifest.Id,
                target,
                remappedTarget,
                ManagedFileRemappingOrigin.Project
            );
        }

        var destination = requestedPacks
            .FirstOrDefault(request =>
                string.Equals(request.Id, pack.Manifest.Id, StringComparison.Ordinal)
            )
            ?.Destination;

        return new ManagedFileTargetResolution(
            destination is null ? target : fileSystem.Path.Combine(destination, target)
        );
    }

    private static ManagedFileTargetResolution CreateRemappedTargetResolution(
        string packId,
        string declaredTarget,
        string effectiveTarget,
        ManagedFileRemappingOrigin origin
    ) =>
        new(
            effectiveTarget,
            new ManagedFileRemapping(packId, declaredTarget, effectiveTarget, origin)
        );

    private ManifestOperationResult<List<ManagedFilePlanCandidate>> CreateManagedFileCandidates(
        DiscoveredPack pack,
        PackManifest.PackManagedFile managedFile,
        string declaredTarget,
        ExternalContentRoots externalContentRoots
    )
    {
        var createdSelector = PackManagedFileSelector.Create(managedFile);
        if (createdSelector.Value is not { } selector)
        {
            return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                $"Pack '{pack.Manifest.Id}': {createdSelector.Error ?? "managed-file mapping has no selector."}"
            );
        }

        var resolvedRoot = ResolveContentRoot(pack, selector, externalContentRoots);
        if (resolvedRoot.Value is not { } contentRoot)
        {
            return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                resolvedRoot.Error ?? "Unable to resolve managed-file content root."
            );
        }

        return selector.Kind switch
        {
            PackManagedFileSelectorKind.File => CreateFileManagedFileCandidate(
                pack,
                contentRoot,
                selector,
                managedFile,
                declaredTarget
            ),
            PackManagedFileSelectorKind.Directory => CreateDirectoryManagedFileCandidates(
                pack,
                contentRoot,
                selector,
                managedFile.Target,
                declaredTarget,
                managedFile.Strategy,
                managedFile.Template
            ),
            _ => CreateGlobManagedFileCandidates(
                pack,
                contentRoot,
                selector,
                managedFile.Target,
                declaredTarget,
                managedFile.Strategy,
                managedFile.Template
            ),
        };
    }

    private static ManifestOperationResult<ManagedFileContentRoot> ResolveContentRoot(
        DiscoveredPack pack,
        PackManagedFileSelector selector,
        ExternalContentRoots externalContentRoots
    )
    {
        if (selector.SourceAlias is not { } alias)
        {
            return ManifestOperationResult<ManagedFileContentRoot>.Success(
                new ManagedFileContentRoot(pack.PackDirectory, null)
            );
        }

        var externalRoot = externalContentRoots.Find(pack.Manifest.Id, alias);
        return externalRoot is null
            ? ManifestOperationResult<ManagedFileContentRoot>.Failure(
                $"Pack '{pack.Manifest.Id}' references source '{alias}' that has not been materialized."
            )
            : ManifestOperationResult<ManagedFileContentRoot>.Success(
                new ManagedFileContentRoot(externalRoot.Directory, externalRoot)
            );
    }

    private ManifestOperationResult<List<ManagedFilePlanCandidate>> CreateFileManagedFileCandidate(
        DiscoveredPack pack,
        ManagedFileContentRoot contentRoot,
        PackManagedFileSelector selector,
        PackManifest.PackManagedFile managedFile,
        string declaredTarget
    )
    {
        var sourcePath = fileSystem.Path.Combine(contentRoot.Directory, selector.Value);
        var contained = EnsureWithinContentRoot(pack, contentRoot, sourcePath, selector.Value);
        if (!contained.IsSuccess)
        {
            return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                contained.Error ?? "Managed-file selector escapes its content root."
            );
        }

        if (!fileSystem.File.Exists(sourcePath))
        {
            return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                contentRoot.External is { } external
                    ? $"Pack '{pack.Manifest.Id}' source '{external.Alias}' file '{selector.Value}' is unavailable."
                    : $"Pack '{pack.Manifest.Id}' source file '{selector.Value}' is unavailable."
            );
        }

        return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Success([
            new ManagedFilePlanCandidate(
                pack,
                contentRoot,
                sourcePath,
                managedFile.Target,
                declaredTarget,
                managedFile.Strategy,
                managedFile.Template
            ),
        ]);
    }

    private ManifestOperationResult<
        List<ManagedFilePlanCandidate>
    > CreateDirectoryManagedFileCandidates(
        DiscoveredPack pack,
        ManagedFileContentRoot contentRoot,
        PackManagedFileSelector selector,
        string targetDirectory,
        string declaredTargetDirectory,
        PackManifest.PackManagedFileStrategy strategy,
        bool isTemplate
    )
    {
        var directory = selector.Value;
        var sourceDirectory = fileSystem.Path.Combine(contentRoot.Directory, directory);
        var contained = EnsureWithinContentRoot(pack, contentRoot, sourceDirectory, directory);
        if (!contained.IsSuccess)
        {
            return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                contained.Error ?? "Managed-file selector escapes its content root."
            );
        }

        if (!fileSystem.Directory.Exists(sourceDirectory))
        {
            return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                $"Pack '{pack.Manifest.Id}' source directory '{directory}' is unavailable."
            );
        }

        var sourceFiles = fileSystem
            .Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new SourceFile(
                path,
                ProjectPath.Normalize(fileSystem.Path.GetRelativePath(sourceDirectory, path))
            ))
            .ToList();
        var retained = ApplyExclusions(sourceFiles, selector.Exclusions);
        return retained.Count == 0
            ? ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                $"Pack '{pack.Manifest.Id}' source directory '{directory}' contains no files."
            )
            : CreateManagedFileCandidates(
                pack,
                contentRoot,
                targetDirectory,
                declaredTargetDirectory,
                sourceDirectory,
                retained,
                selector.Flatten,
                strategy,
                isTemplate
            );
    }

    private ManifestOperationResult<List<ManagedFilePlanCandidate>> CreateGlobManagedFileCandidates(
        DiscoveredPack pack,
        ManagedFileContentRoot contentRoot,
        PackManagedFileSelector selector,
        string targetDirectory,
        string declaredTargetDirectory,
        PackManifest.PackManagedFileStrategy strategy,
        bool isTemplate
    )
    {
        var glob = selector.Value;
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(ProjectPath.Normalize(glob));
        var sourcePaths = fileSystem
            .Directory.EnumerateFiles(contentRoot.Directory, "*", SearchOption.AllDirectories)
            .Select(path => new SourceFile(
                path,
                ProjectPath.Normalize(fileSystem.Path.GetRelativePath(contentRoot.Directory, path))
            ))
            .ToList();
        var sourcePathsByRelativePath = sourcePaths.ToDictionary(
            sourcePath => sourcePath.RelativePath,
            StringComparer.Ordinal
        );
        var matchedSourcePaths = matcher
            .Match(sourcePathsByRelativePath.Keys)
            .Files.Select(match => sourcePathsByRelativePath[ProjectPath.Normalize(match.Path)])
            .OrderBy(sourcePath => sourcePath.RelativePath, StringComparer.Ordinal)
            .ToList();
        var retained = ApplyExclusions(matchedSourcePaths, selector.Exclusions);

        var globBaseDirectory = GetGlobBaseDirectory(contentRoot.Directory, glob);
        return retained.Count == 0
            ? ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                $"Pack '{pack.Manifest.Id}' glob '{glob}' matches no files."
            )
            : CreateManagedFileCandidates(
                pack,
                contentRoot,
                targetDirectory,
                declaredTargetDirectory,
                globBaseDirectory,
                [
                    .. retained.Select(sourceFile =>
                        sourceFile with
                        {
                            RelativePath = ProjectPath.Normalize(
                                fileSystem.Path.GetRelativePath(globBaseDirectory, sourceFile.Path)
                            ),
                        }
                    ),
                ],
                selector.Flatten,
                strategy,
                isTemplate
            );
    }

    private static List<SourceFile> ApplyExclusions(
        IReadOnlyList<SourceFile> sourceFiles,
        IReadOnlyList<string> exclusions
    )
    {
        if (exclusions.Count == 0)
        {
            return [.. sourceFiles];
        }

        var matcher = new Matcher(StringComparison.Ordinal);
        foreach (var exclusion in exclusions)
        {
            matcher.AddInclude(ProjectPath.Normalize(exclusion));
        }

        var candidates = sourceFiles.ToDictionary(
            sourceFile => sourceFile.RelativePath,
            StringComparer.Ordinal
        );
        var excluded = matcher
            .Match(candidates.Keys)
            .Files.Select(match => ProjectPath.Normalize(match.Path))
            .ToHashSet(StringComparer.Ordinal);
        return [.. sourceFiles.Where(sourceFile => !excluded.Contains(sourceFile.RelativePath))];
    }

    private ManifestOperationResult<bool> EnsureWithinContentRoot(
        DiscoveredPack pack,
        ManagedFileContentRoot contentRoot,
        string candidatePath,
        string declaredPath
    )
    {
        var root = fileSystem.Path.GetFullPath(contentRoot.Directory);
        var resolved = fileSystem.Path.GetFullPath(candidatePath);
        var comparison =
            fileSystem.Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        var rootWithSeparator = root.EndsWith(
            fileSystem.Path.DirectorySeparatorChar.ToString(),
            comparison
        )
            ? root
            : $"{root}{fileSystem.Path.DirectorySeparatorChar}";
        return
            string.Equals(resolved, root, comparison)
            || resolved.StartsWith(rootWithSeparator, comparison)
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(
                $"Pack '{pack.Manifest.Id}' selector '{declaredPath}' resolves outside its content root."
            );
    }

    private ManifestOperationResult<List<ManagedFilePlanCandidate>> CreateManagedFileCandidates(
        DiscoveredPack pack,
        ManagedFileContentRoot contentRoot,
        string targetDirectory,
        string declaredTargetDirectory,
        string sourceDirectory,
        IReadOnlyList<SourceFile> sourceFiles,
        bool flatten,
        PackManifest.PackManagedFileStrategy strategy,
        bool isTemplate
    )
    {
        var candidates = new List<ManagedFilePlanCandidate>(sourceFiles.Count);
        var flattenedNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sourceFile in sourceFiles)
        {
            var sourcePathRelativeToDirectory = fileSystem.Path.GetRelativePath(
                sourceDirectory,
                sourceFile.Path
            );
            if (flatten)
            {
                var fileName = fileSystem.Path.GetFileName(sourceFile.Path);
                if (
                    flattenedNames.TryGetValue(fileName, out var conflictingPath)
                    && !string.Equals(conflictingPath, sourceFile.Path, StringComparison.Ordinal)
                )
                {
                    return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Failure(
                        $"Pack '{pack.Manifest.Id}' cannot flatten '{ProjectPath.Normalize(sourcePathRelativeToDirectory)}' because file name '{fileName}' is already claimed."
                    );
                }

                flattenedNames[fileName] = sourceFile.Path;
                sourcePathRelativeToDirectory = fileName;
            }

            var declaredTargetPath = fileSystem.Path.Combine(
                declaredTargetDirectory,
                sourcePathRelativeToDirectory
            );
            var targetPath = fileSystem.Path.Combine(
                targetDirectory,
                sourcePathRelativeToDirectory
            );
            candidates.Add(
                new ManagedFilePlanCandidate(
                    pack,
                    contentRoot,
                    sourceFile.Path,
                    targetPath,
                    declaredTargetPath,
                    strategy,
                    isTemplate
                )
            );
        }

        return ManifestOperationResult<List<ManagedFilePlanCandidate>>.Success(candidates);
    }

    private ManifestOperationResult<PlannedManagedFile> CreateManagedFilePlan(
        string projectDirectory,
        DiscoveredPack pack,
        ManagedFileContentRoot contentRoot,
        string sourcePath,
        string target,
        string declaredTarget,
        PackManifest.PackManagedFileStrategy strategy,
        bool isTemplate,
        Dictionary<string, List<ManagedRootOwner>> existingManagedTargets,
        PackInstallationRequest installationRequest,
        ResolvedPackParameters parameters,
        ManagedFileTemplateContext templateContext,
        List<ManagedFileTemplateDiagnostic> diagnostics
    )
    {
        var renderedContent = templateRenderer.RenderManagedFile(
            sourcePath,
            isTemplate,
            parameters,
            templateContext
        );
        if (renderedContent.Value is not { } rendered)
        {
            return ManifestOperationResult<PlannedManagedFile>.Failure(
                renderedContent.Error ?? "Unable to render managed file."
            );
        }

        var content = rendered.Contents;
        diagnostics.AddRange(rendered.Diagnostics);
        var targetPath = fileSystem.Path.GetFullPath(target, projectDirectory);
        var targetPathRelativeToProject = ProjectPath.Normalize(
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
                ProjectPath.Normalize(declaredTarget),
                content,
                targetPath,
                targetPathRelativeToProject,
                strategy,
                CreateExternalProvenance(contentRoot, sourcePath)
            )
        );
    }

    private PlannedExternalSource? CreateExternalProvenance(
        ManagedFileContentRoot contentRoot,
        string sourcePath
    ) =>
        contentRoot.External is not { } external
            ? null
            : new PlannedExternalSource(
                external.Alias,
                external.SourceName,
                external.Fingerprint,
                ProjectPath.Normalize(
                    fileSystem.Path.GetRelativePath(contentRoot.Directory, sourcePath)
                ),
                external.Ref,
                external.ResolvedCommit
            );

    private bool RenderedContentMatchesTarget(byte[] renderedContent, string targetPath) =>
        CryptographicOperations.FixedTimeEquals(
            renderedContent,
            fileSystem.File.ReadAllBytes(targetPath)
        );

    private string GetGlobBaseDirectory(string packDirectory, string glob)
    {
        var baseDirectory = packDirectory;
        foreach (
            var segment in ProjectPath
                .Normalize(glob)
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
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

    private static bool IsMergeStrategy(PackManifest.PackManagedFileStrategy strategy) =>
        string.Equals(strategy.Type, "merge", StringComparison.Ordinal);

    private sealed record SourceFile(string Path, string RelativePath);
}
