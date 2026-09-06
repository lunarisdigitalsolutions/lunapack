using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Packs;

internal sealed class PackParameterBindingExpression(
    Func<IReadOnlyDictionary<string, ResolvedPackParameterValue>, object> evaluator,
    PackParameterType type,
    bool multiple,
    IReadOnlySet<string> referencedParameters
)
{
    public PackParameterType Type { get; } = type;

    public bool Multiple { get; } = multiple;

    public IReadOnlySet<string> ReferencedParameters { get; } = referencedParameters;

    public ManifestOperationResult<object> Evaluate(
        IReadOnlyDictionary<string, ResolvedPackParameterValue> values
    )
    {
        var unresolved = ReferencedParameters.FirstOrDefault(parameter =>
            !values.ContainsKey(parameter)
        );
        return unresolved is null
            ? ManifestOperationResult<object>.Success(evaluator(values))
            : ManifestOperationResult<object>.Failure(
                $"Parameter binding expression requires unresolved parameter '{unresolved}'."
            );
    }
}
