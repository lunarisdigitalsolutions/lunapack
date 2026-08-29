using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record PolicyDeniedLifecycleHook(
    LifecycleHookInvocation Invocation,
    IReadOnlyList<ScriptDenialOrigin> DenyingScopes
);
