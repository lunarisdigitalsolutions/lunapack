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
        foreach (var resolvedPack in lockFile.Packs)
        {
            roots.Add(
                new ManagedRoot(
                    new ManagedRootOwner(
                        ManagedRootKind.Pack,
                        resolvedPack.Id,
                        resolvedPack.Version
                    ),
                    resolvedPack.SourceName ?? string.Empty,
                    resolvedPack.SourceIdentity,
                    resolvedPack.GitSource,
                    [
                        .. resolvedPack.ManagedFiles.Select(managedFile => new ManagedRootFile(
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

    public static IReadOnlyList<ManagedRoot> FromInstallationPlan(
        ResolvedPackGraph graph,
        PackInstallationPlan installationPlan
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(installationPlan);

        return
        [
            .. graph.Packs.Select(pack => new ManagedRoot(
                new ManagedRootOwner(ManagedRootKind.Pack, pack.Manifest.Id, pack.Manifest.Version),
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
                    && owners.Find(owner => owner.Kind != plannedRoot.Owner.Kind) is { } conflicting
                )
                {
                    return $"Target '{file.TargetPath}' is already managed by {conflicting.Describe()}.";
                }
            }
        }

        return null;
    }

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
