namespace Lunapack.Cli.Packs;

internal readonly record struct LifecycleConditionContext(
    bool ScriptsSkipped,
    LifecycleScriptState PreviousScriptState
);
