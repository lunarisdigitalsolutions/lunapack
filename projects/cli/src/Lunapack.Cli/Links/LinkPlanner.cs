using System.IO.Abstractions;
using System.Security.Cryptography;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Links;

internal sealed class LinkPlanner(IFileSystem fileSystem)
{
    public ManifestOperationResult<PackUpdatePlan> Plan(
        string projectDirectory,
        LinkResolution resolution,
        ProjectLockFile lockFile,
        bool adoptExisting,
        IReadOnlySet<string>? ignoredDeclaredTargets = null
    )
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(lockFile);

        var snapshot = resolution.Snapshot;
        var owner = new ManagedRootOwner(ManagedRootKind.Link, snapshot.Name);
        var ownership = ManagedRootInventory.CreateOwnershipMap(lockFile, fileSystem);
        var actions = new List<PlannedPackUpdateAction>();
        foreach (var file in snapshot.Files)
        {
            var targetPath = fileSystem.Path.GetFullPath(file.TargetPath, projectDirectory);
            var conflict = FindConflict(owner, file, targetPath, ownership, adoptExisting);
            if (conflict is not null)
            {
                return ManifestOperationResult<PackUpdatePlan>.Failure(conflict);
            }

            actions.Add(
                new WriteManagedRootFileUpdateAction(
                    owner,
                    new ManagedRootFile(
                        file.SourcePath,
                        file.DeclaredTargetPath,
                        file.TargetPath,
                        file.Sha256
                    ),
                    targetPath,
                    resolution.ReadContents(file)
                )
            );
        }

        var removals = PlanRemovals(
            projectDirectory,
            snapshot,
            lockFile,
            owner,
            ignoredDeclaredTargets ?? new HashSet<string>(StringComparer.Ordinal)
        );
        if (!removals.IsSuccess)
        {
            return ManifestOperationResult<PackUpdatePlan>.Failure(
                removals.Error ?? "Unable to plan link removals."
            );
        }

        actions.AddRange(removals.Value ?? []);
        return ManifestOperationResult<PackUpdatePlan>.Success(new PackUpdatePlan(actions));
    }

    private ManifestOperationResult<List<PlannedPackUpdateAction>> PlanRemovals(
        string projectDirectory,
        ResolvedLinkSnapshot snapshot,
        ProjectLockFile lockFile,
        ManagedRootOwner owner,
        IReadOnlySet<string> ignoredDeclaredTargets
    )
    {
        if (!lockFile.Links.TryGetValue(snapshot.Name, out var lockedLink))
        {
            return ManifestOperationResult<List<PlannedPackUpdateAction>>.Success([]);
        }

        var pathComparer = ProjectPath.ProjectRelativePathComparer(fileSystem);
        var plannedTargets = new HashSet<string>(pathComparer);
        foreach (var file in snapshot.Files)
        {
            var normalized = ProjectPath.NormalizeProjectRelativePath(
                fileSystem,
                projectDirectory,
                file.TargetPath
            );
            if (normalized.Value is not { } normalizedTarget)
            {
                return ManifestOperationResult<List<PlannedPackUpdateAction>>.Failure(
                    normalized.Error ?? $"Invalid link target '{file.TargetPath}'."
                );
            }

            plannedTargets.Add(normalizedTarget);
        }

        var actions = new List<PlannedPackUpdateAction>();
        foreach (var lockedFile in lockedLink.Files)
        {
            var normalized = ProjectPath.NormalizeProjectRelativePath(
                fileSystem,
                projectDirectory,
                lockedFile.TargetPath
            );
            if (normalized.Value is not { } targetPathRelativeToProject)
            {
                return ManifestOperationResult<List<PlannedPackUpdateAction>>.Failure(
                    normalized.Error ?? $"Invalid locked link target '{lockedFile.TargetPath}'."
                );
            }

            var targetIsRetained =
                plannedTargets.Contains(targetPathRelativeToProject)
                || ignoredDeclaredTargets.Contains(lockedFile.DeclaredTargetPath);
            if (targetIsRetained)
            {
                continue;
            }

            var plannedRemoval = PlanRemoval(
                projectDirectory,
                owner,
                lockedFile,
                targetPathRelativeToProject
            );
            if (!plannedRemoval.IsSuccess)
            {
                return ManifestOperationResult<List<PlannedPackUpdateAction>>.Failure(
                    plannedRemoval.Error ?? "Unable to plan link target removal."
                );
            }

            actions.AddRange(plannedRemoval.Value ?? []);
        }

        return ManifestOperationResult<List<PlannedPackUpdateAction>>.Success(actions);
    }

    private ManifestOperationResult<List<PlannedPackUpdateAction>> PlanRemoval(
        string projectDirectory,
        ManagedRootOwner owner,
        ProjectLockFile.LinkFile lockedFile,
        string targetPathRelativeToProject
    )
    {
        var targetPath = fileSystem.Path.GetFullPath(targetPathRelativeToProject, projectDirectory);
        try
        {
            ProjectMutationPathSecurity.EnsureNoAliases(fileSystem, projectDirectory, targetPath);
            if (!fileSystem.File.Exists(targetPath))
            {
                return ManifestOperationResult<List<PlannedPackUpdateAction>>.Success([]);
            }

            var targetSha256 = Convert.ToHexString(
                SHA256.HashData(fileSystem.File.ReadAllBytes(targetPath))
            );
            var previousFile = new ManagedRootFile(
                lockedFile.SourcePath,
                lockedFile.DeclaredTargetPath,
                lockedFile.TargetPath,
                lockedFile.Sha256
            );
            PlannedPackUpdateAction action = string.Equals(
                targetSha256,
                lockedFile.Sha256,
                StringComparison.OrdinalIgnoreCase
            )
                ? new DeleteManagedFileUpdateAction(owner, previousFile, targetPath, targetSha256)
                : new PreserveManagedFileUpdateAction(owner, previousFile, targetPath);
            return ManifestOperationResult<List<PlannedPackUpdateAction>>.Success([action]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<List<PlannedPackUpdateAction>>.Failure(
                $"Unable to inspect obsolete link target '{targetPathRelativeToProject}': {exception.Message}"
            );
        }
    }

    private string? FindConflict(
        ManagedRootOwner owner,
        ResolvedLinkFile file,
        string targetPath,
        Dictionary<string, List<ManagedRootOwner>> ownership,
        bool adoptExisting
    )
    {
        if (!fileSystem.File.Exists(targetPath))
        {
            return null;
        }

        if (ownership.TryGetValue(ProjectPath.Normalize(file.TargetPath), out var owners))
        {
            return owners.Find(candidate => !candidate.Matches(owner)) is { } conflicting
                ? $"Target '{file.TargetPath}' is already managed by {conflicting.Describe()}."
                : null;
        }

        if (!adoptExisting)
        {
            return $"Target '{file.TargetPath}' already exists and is not managed by LunaPack.";
        }

        return string.Equals(
            Convert.ToHexString(SHA256.HashData(fileSystem.File.ReadAllBytes(targetPath))),
            file.Sha256,
            StringComparison.OrdinalIgnoreCase
        )
            ? null
            : $"Target '{file.TargetPath}' differs from the link content and cannot be adopted.";
    }
}
