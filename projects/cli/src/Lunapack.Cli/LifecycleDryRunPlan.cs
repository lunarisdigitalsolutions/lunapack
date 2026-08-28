namespace Lunapack.Cli;

internal sealed record LifecycleDryRunPlan(
    ScriptExecutionMode ScriptMode,
    IReadOnlyList<LifecycleHookInvocation> PreMutation,
    IReadOnlyList<LifecycleHookInvocation> PostMutation,
    IReadOnlyList<PackLifecyclePlan.Entry> Changes,
    IReadOnlyList<ScriptDenialOrigin>? ScriptDenialScopes = null
);
