namespace Lunapack.Cli;

internal sealed record AuthorizedLifecycleHooks(
    IReadOnlyList<ResolvedLifecycleHookInvocation> PreMutation,
    IReadOnlyList<ResolvedLifecycleHookInvocation> PostMutation
);
