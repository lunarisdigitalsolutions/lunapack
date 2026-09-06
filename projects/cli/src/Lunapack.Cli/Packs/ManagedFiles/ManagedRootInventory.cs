using System.Security.Cryptography;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.ManagedFiles;

internal static class ManagedRootInventory
{
    public static IReadOnlyList<ManagedRoot> FromLockFile(ProjectLockFile lockFile)
    {
        ArgumentNullException.ThrowIfNull(lockFile);

        var roots = new List<ManagedRoot>();
        var instanceRootKeys = lockFile
            .Instances.Select(instance => instance.RootResolution)
            .ToHashSet();
        foreach (var instance in lockFile.Instances)
        {
            roots.Add(
                new ManagedRoot(
                    new ManagedRootOwner(
                        ManagedRootKind.PackInstance,
                        instance.Id,
                        instance.RootResolution.Version,
                        instance.Name
                    ),
                    string.Empty,
                    instance.RootResolution.SourceIdentity,
                    null,
                    [
                        .. instance.ManagedFiles.Select(managedFile => new ManagedRootFile(
                            string.Empty,
                            managedFile.DeclaredTargetPath ?? managedFile.TargetPath,
                            managedFile.TargetPath,
                            managedFile.Sha256
                        )),
                    ]
                )
            );
        }

        foreach (var resolvedPack in lockFile.Packs)
        {
            var managedFiles = GetResolvedManagedFiles(resolvedPack, instanceRootKeys);
            roots.Add(
                new ManagedRoot(
                    new ManagedRootOwner(
                        ManagedRootKind.Pack,
                        resolvedPack.Id,
                        resolvedPack.Version,
                        IsLegacy: lockFile.SchemaVersion == 1
                    ),
                    resolvedPack.SourceName ?? string.Empty,
                    resolvedPack.SourceIdentity,
                    resolvedPack.GitSource,
                    [
                        .. managedFiles.Select(managedFile => new ManagedRootFile(
                            resolvedPack.PackPath,
                            managedFile.DeclaredTargetPath ?? managedFile.TargetPath,
                            managedFile.TargetPath,
                            managedFile.Sha256
                        )),
                    ]
                )
            );
        }

        foreach (var (name, resolvedLink) in lockFile.Links)
        {
            roots.Add(
                new ManagedRoot(
                    new ManagedRootOwner(ManagedRootKind.Link, name),
                    resolvedLink.SourceName,
                    resolvedLink.SourceIdentity,
                    resolvedLink.GitSource,
                    [
                        .. resolvedLink.Files.Select(file => new ManagedRootFile(
                            file.SourcePath,
                            file.DeclaredTargetPath,
                            file.TargetPath,
                            file.Sha256
                        )),
                    ]
                )
            );
        }

        return roots;
    }

    private static List<ProjectLockFile.ManagedFile> GetResolvedManagedFiles(
        ProjectLockFile.ResolvedPack resolvedPack,
        HashSet<ProjectLockFile.ResolvedPackKey> instanceRootKeys
    ) =>
        resolvedPack.Key is not null && instanceRootKeys.Contains(resolvedPack.Key)
            ? []
            : resolvedPack.ManagedFiles;

    public static IReadOnlyList<ManagedRoot> FromInstallationPlan(
        ResolvedPackGraph graph,
        PackInstallationPlan installationPlan,
        PackInstanceIdentity instanceIdentity
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(installationPlan);

        return
        [
            .. graph.Packs.Select(pack => new ManagedRoot(
                graph.IsRoot(pack)
                    ? new ManagedRootOwner(
                        ManagedRootKind.PackInstance,
                        instanceIdentity.PackId,
                        pack.Manifest.Version,
                        instanceIdentity.Alias
                    )
                    : new ManagedRootOwner(
                        ManagedRootKind.Pack,
                        pack.Manifest.Id,
                        pack.Manifest.Version
                    ),
                pack.SourceName,
                pack.SourceIdentity,
                pack.GitSource,
                [
                    .. installationPlan
                        .ManagedFiles.Where(managedFile =>
                            string.Equals(
                                managedFile.Pack.Manifest.Id,
                                pack.Manifest.Id,
                                StringComparison.Ordinal
                            )
                            && string.Equals(
                                managedFile.Pack.Manifest.Version,
                                pack.Manifest.Version,
                                StringComparison.Ordinal
                            )
                        )
                        .Select(managedFile => new ManagedRootFile(
                            managedFile.SourcePath,
                            managedFile.DeclaredTargetPath,
                            managedFile.TargetPathRelativeToProject,
                            Convert.ToHexString(SHA256.HashData(managedFile.Contents))
                        )),
                ]
            )),
        ];
    }

    public static string? FindCrossRootCollision(
        IReadOnlyList<ManagedRoot> plannedRoots,
        ProjectLockFile lockFile
    )
    {
        ArgumentNullException.ThrowIfNull(plannedRoots);

        var ownership = CreateOwnershipMap(lockFile);
        foreach (var plannedRoot in plannedRoots)
        {
            foreach (var file in plannedRoot.Files)
            {
                if (
                    ownership.TryGetValue(ProjectPath.Normalize(file.TargetPath), out var owners)
                    && owners.Find(owner => IsConflictingOwner(owner, plannedRoot.Owner))
                        is { } conflicting
                )
                {
                    return $"Target '{file.TargetPath}' is already managed by {conflicting.Describe()}.";
                }
            }
        }

        return null;
    }

    private static bool IsConflictingOwner(
        ManagedRootOwner existingOwner,
        ManagedRootOwner plannedOwner
    ) =>
        !existingOwner.Matches(plannedOwner)
        && !(
            existingOwner.Kind == ManagedRootKind.Pack && plannedOwner.Kind == ManagedRootKind.Pack
        );

    public static Dictionary<string, List<ManagedRootOwner>> CreateOwnershipMap(
        ProjectLockFile lockFile
    )
    {
        var ownership = new Dictionary<string, List<ManagedRootOwner>>(StringComparer.Ordinal);
        foreach (var root in FromLockFile(lockFile))
        {
            foreach (var file in root.Files)
            {
                var targetPath = ProjectPath.Normalize(file.TargetPath);
                if (!ownership.TryGetValue(targetPath, out var owners))
                {
                    owners = [];
                    ownership.Add(targetPath, owners);
                }

                owners.Add(root.Owner);
            }
        }

        return ownership;
    }
}
