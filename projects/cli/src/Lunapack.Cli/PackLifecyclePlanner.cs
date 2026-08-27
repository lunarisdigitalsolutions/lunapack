namespace Lunapack.Cli;

internal sealed class PackLifecyclePlanner
{
    public static PackLifecyclePlan Plan(ResolvedPackGraph graph, ProjectLockFile previousLockFile)
    {
        var previousPacks = previousLockFile.Packs.ToDictionary(
            pack => pack.Id,
            StringComparer.Ordinal
        );
        var changes = new List<PackLifecyclePlan.Entry>(
            graph.Packs.Count + previousLockFile.Packs.Count
        );
        foreach (var pack in graph.Packs)
        {
            previousPacks.Remove(pack.Manifest.Id, out var previousPack);
            changes.Add(
                new PackLifecyclePlan.Entry(
                    GetChangeKind(pack, previousPack),
                    pack,
                    previousPack,
                    graph.IsRoot(pack),
                    GetDisabledHooks(graph, pack)
                )
            );
        }

        foreach (var previousPack in previousLockFile.Packs)
        {
            if (previousPacks.ContainsKey(previousPack.Id))
            {
                changes.Add(
                    new PackLifecyclePlan.Entry(
                        PackLifecyclePlan.ChangeKind.Removed,
                        null,
                        previousPack,
                        false,
                        new HashSet<string>(StringComparer.Ordinal)
                    )
                );
            }
        }

        var executableChanges = changes
            .Where(change =>
                change.Kind
                    is PackLifecyclePlan.ChangeKind.Install
                        or PackLifecyclePlan.ChangeKind.Update
            )
            .ToList();
        return new PackLifecyclePlan(changes, executableChanges, executableChanges);
    }

    public static PackLifecyclePlan PlanRemoval(
        ResolvedPackGraph graph,
        ProjectLockFile previousLockFile,
        IReadOnlySet<string> removedPackIds
    )
    {
        var previousPacks = previousLockFile.Packs.ToDictionary(
            pack => pack.Id,
            StringComparer.Ordinal
        );
        var removals = graph
            .Packs.Where(pack => removedPackIds.Contains(pack.Manifest.Id))
            .Select(pack => new PackLifecyclePlan.Entry(
                PackLifecyclePlan.ChangeKind.Removed,
                pack,
                previousPacks.GetValueOrDefault(pack.Manifest.Id),
                graph.IsRoot(pack),
                GetDisabledHooks(graph, pack)
            ))
            .ToList();
        return new PackLifecyclePlan(removals, removals, removals);
    }

    private static PackLifecyclePlan.ChangeKind GetChangeKind(
        DiscoveredPack incomingPack,
        ProjectLockFile.ResolvedPack? previousPack
    )
    {
        if (previousPack is null)
        {
            return PackLifecyclePlan.ChangeKind.Install;
        }

        return string.Equals(
            incomingPack.Manifest.Version,
            previousPack.Version,
            StringComparison.Ordinal
        )
            ? PackLifecyclePlan.ChangeKind.Unchanged
            : PackLifecyclePlan.ChangeKind.Update;
    }

    private static HashSet<string> GetDisabledHooks(ResolvedPackGraph graph, DiscoveredPack pack) =>
        graph.IsRoot(pack)
            ? new HashSet<string>(StringComparer.Ordinal)
            : graph
                .GetIncomingReferences(pack)
                .SelectMany(reference => reference.DisabledHooks)
                .ToHashSet(StringComparer.Ordinal);
}
