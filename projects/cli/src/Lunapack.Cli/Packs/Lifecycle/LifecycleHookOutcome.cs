namespace Lunapack.Cli.Packs.Lifecycle;

internal enum LifecycleHookOutcome
{
    Succeeded,
    Failed,
    TimedOut,
    Cancelled,
}
