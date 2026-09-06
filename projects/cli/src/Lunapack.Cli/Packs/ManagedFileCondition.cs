namespace Lunapack.Cli.Packs;

internal sealed class ManagedFileCondition(
    Func<
        IReadOnlyDictionary<string, ResolvedPackParameterValue>,
        LifecycleConditionContext,
        bool
    > evaluator,
    IReadOnlySet<string>? referencedParameters = null,
    bool dependsOnRuntimeState = false,
    bool dependsOnPreviousScriptState = false
)
{
    public ManagedFileCondition(
        Func<IReadOnlyDictionary<string, ResolvedPackParameterValue>, bool> evaluator,
        IReadOnlySet<string>? referencedParameters = null
    )
        : this((values, _) => evaluator(values), referencedParameters) { }

    public IReadOnlySet<string> ReferencedParameters { get; } =
        referencedParameters ?? new HashSet<string>(StringComparer.Ordinal);

    public bool DependsOnRuntimeState { get; } = dependsOnRuntimeState;

    public bool DependsOnPreviousScriptState { get; } = dependsOnPreviousScriptState;

    public bool Evaluate(
        IReadOnlyDictionary<string, ResolvedPackParameterValue> values,
        LifecycleConditionContext context = default
    ) => evaluator(values, context);
}
