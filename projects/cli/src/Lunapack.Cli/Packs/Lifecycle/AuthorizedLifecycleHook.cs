namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record AuthorizedLifecycleHook(
    LifecycleHookInvocation Invocation,
    ResolvedLifecycleHookInvocation? Script
);
