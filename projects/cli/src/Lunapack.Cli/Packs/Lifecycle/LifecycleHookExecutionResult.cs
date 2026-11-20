namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record LifecycleHookExecutionResult(
    LifecycleHookOutcome Outcome,
    string? Error,
    int? ExitCode = null,
    string? HistoryOutcome = null
)
{
    public bool IsSuccess => Outcome == LifecycleHookOutcome.Succeeded;

    public LifecycleHookExecutionResult WithAdditionalError(string additionalError) =>
        this with
        {
            Error = string.Join(
                " ",
                new[] { Error, additionalError }.Where(value => !string.IsNullOrWhiteSpace(value))
            ),
        };

    public static LifecycleHookExecutionResult Success(int? exitCode = 0) =>
        new(LifecycleHookOutcome.Succeeded, null, exitCode, "succeeded");

    public static LifecycleHookExecutionResult Failure(
        string error,
        int? exitCode = null,
        string? historyOutcome = "failed"
    ) => new(LifecycleHookOutcome.Failed, error, exitCode, historyOutcome);

    public static LifecycleHookExecutionResult Timeout(string error, int? exitCode = null) =>
        new(LifecycleHookOutcome.TimedOut, error, exitCode, "failed");

    public static LifecycleHookExecutionResult Cancelled(string error, int? exitCode = null) =>
        new(LifecycleHookOutcome.Cancelled, error, exitCode, "canceled");
}
