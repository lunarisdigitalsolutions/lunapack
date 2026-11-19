namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record HookExitCodeResult(int? Value, LifecycleHookExecutionResult? Failure)
{
    public static HookExitCodeResult Failed(string error) =>
        new(null, LifecycleHookExecutionResult.Failure(error));
}
