namespace Lunapack.Cli.Packs;

internal sealed class ManagedFileCondition(
    Func<IReadOnlyDictionary<string, ResolvedPackParameterValue>, bool> evaluator,
    IReadOnlySet<string>? referencedParameters = null
)
{
    public IReadOnlySet<string> ReferencedParameters { get; } =
        referencedParameters ?? new HashSet<string>(StringComparer.Ordinal);

    public bool Evaluate(IReadOnlyDictionary<string, ResolvedPackParameterValue> values) =>
        evaluator(values);
}
