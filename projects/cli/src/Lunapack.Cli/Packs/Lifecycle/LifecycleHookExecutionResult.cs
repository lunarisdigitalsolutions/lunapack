namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record LifecycleHookExecutionResult(LifecycleHookOutcome Outcome, string? Error)
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

    public static LifecycleHookExecutionResult Success() =>
        new(LifecycleHookOutcome.Succeeded, null);

    public static LifecycleHookExecutionResult Failure(string error) =>
        new(LifecycleHookOutcome.Failed, error);

    public static LifecycleHookExecutionResult Timeout(string error) =>
        new(LifecycleHookOutcome.TimedOut, error);

    public static LifecycleHookExecutionResult Cancelled(string error) =>
        new(LifecycleHookOutcome.Cancelled, error);
}
