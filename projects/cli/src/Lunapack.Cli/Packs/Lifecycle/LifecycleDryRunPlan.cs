using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record LifecycleDryRunPlan(
    ScriptExecutionMode ScriptMode,
    IReadOnlyList<LifecycleHookInvocation> PreMutation,
    IReadOnlyList<LifecycleHookInvocation> PostMutation,
    IReadOnlyList<PackLifecyclePlan.Entry> Changes,
    IReadOnlyList<ScriptDenialOrigin>? ScriptDenialScopes = null
);
