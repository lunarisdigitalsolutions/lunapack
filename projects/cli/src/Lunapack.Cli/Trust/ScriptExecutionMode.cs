using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Trust;

internal sealed record ScriptExecutionMode(string Value)
{
    public static ScriptExecutionMode Prompt { get; } = new("prompt");

    public static ScriptExecutionMode Run { get; } = new("run");

    public static ScriptExecutionMode Skip { get; } = new("skip");

    public static ManifestOperationResult<ScriptExecutionMode> Parse(string value) =>
        value switch
        {
            "prompt" => ManifestOperationResult<ScriptExecutionMode>.Success(Prompt),
            "run" => ManifestOperationResult<ScriptExecutionMode>.Success(Run),
            "skip" => ManifestOperationResult<ScriptExecutionMode>.Success(Skip),
            _ => ManifestOperationResult<ScriptExecutionMode>.Failure(
                $"Unsupported script mode '{value}'. Expected prompt, run, or skip."
            ),
        };
}
