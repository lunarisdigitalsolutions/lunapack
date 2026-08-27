namespace Lunapack.Cli;

internal sealed record AuthorizedLifecycleHook(
    LifecycleHookInvocation Invocation,
    ResolvedLifecycleHookInvocation? Script
);
