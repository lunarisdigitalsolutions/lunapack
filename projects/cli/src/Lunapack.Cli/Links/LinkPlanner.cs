using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Lunapack.Cli;

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
        var ownership = ManagedRootInventory.CreateOwnershipMap(lockFile);
        var actions = new List<PlannedPackUpdateAction>();
        foreach (var file in snapshot.Files)
        {
            var targetPath = fileSystem.Path.GetFullPath(file.TargetPath, projectDirectory);
            var conflict = FindConflict(
                owner,
                file,
                targetPath,
                ownership,
                adoptExisting,
                resolution
            );
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

        actions.AddRange(
            PlanRemovals(
                projectDirectory,
                snapshot,
                lockFile,
                owner,
                ignoredDeclaredTargets ?? new HashSet<string>(StringComparer.Ordinal)
            )
        );
        return ManifestOperationResult<PackUpdatePlan>.Success(new PackUpdatePlan(actions));
    }

    private IEnumerable<PlannedPackUpdateAction> PlanRemovals(
        string projectDirectory,
        ResolvedLinkSnapshot snapshot,
        ProjectLockFile lockFile,
        ManagedRootOwner owner,
        IReadOnlySet<string> ignoredDeclaredTargets
    )
    {
        if (!lockFile.Links.TryGetValue(snapshot.Name, out var lockedLink))
        {
            yield break;
        }

        var plannedTargets = snapshot
            .Files.Select(file => file.TargetPath)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var lockedFile in lockedLink.Files)
        {
            if (
                plannedTargets.Contains(lockedFile.TargetPath)
                || ignoredDeclaredTargets.Contains(lockedFile.DeclaredTargetPath)
            )
            {
                continue;
            }

            yield return new DeleteManagedFileUpdateAction(
                owner,
                new ManagedRootFile(
                    lockedFile.SourcePath,
                    lockedFile.DeclaredTargetPath,
                    lockedFile.TargetPath,
                    lockedFile.Sha256
                ),
                fileSystem.Path.GetFullPath(lockedFile.TargetPath, projectDirectory)
            );
        }
    }

    private string? FindConflict(
        ManagedRootOwner owner,
        ResolvedLinkFile file,
        string targetPath,
        Dictionary<string, List<ManagedRootOwner>> ownership,
        bool adoptExisting,
        LinkResolution resolution
    )
    {
        if (!fileSystem.File.Exists(targetPath))
        {
            return null;
        }

        if (ownership.TryGetValue(file.TargetPath, out var owners))
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
