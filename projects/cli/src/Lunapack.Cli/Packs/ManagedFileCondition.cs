namespace Lunapack.Cli;

internal sealed class ManagedFileCondition(
    Func<IReadOnlyDictionary<string, ResolvedPackParameterValue>, bool> evaluator
)
{
    public bool Evaluate(IReadOnlyDictionary<string, ResolvedPackParameterValue> values) =>
        evaluator(values);
}
