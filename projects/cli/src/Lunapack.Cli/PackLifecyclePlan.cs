namespace Lunapack.Cli;

internal sealed record PackLifecyclePlan(
    IReadOnlyList<PackLifecyclePlan.Entry> Changes,
    IReadOnlyList<PackLifecyclePlan.Entry> PreMutation,
    IReadOnlyList<PackLifecyclePlan.Entry> PostMutation
)
{
    internal sealed record Entry(
        ChangeKind Kind,
        DiscoveredPack? IncomingPack,
        ProjectLockFile.ResolvedPack? PreviousPack,
        bool IsDirectRoot,
        IReadOnlySet<string> DisabledHooks
    );

    internal enum ChangeKind
    {
        Install,
        Update,
        Unchanged,
        Removed,
    }
}
