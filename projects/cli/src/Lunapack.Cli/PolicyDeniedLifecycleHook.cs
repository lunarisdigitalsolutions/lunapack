namespace Lunapack.Cli;

internal sealed record PolicyDeniedLifecycleHook(
    LifecycleHookInvocation Invocation,
    IReadOnlyList<ScriptDenialOrigin> DenyingScopes
);
