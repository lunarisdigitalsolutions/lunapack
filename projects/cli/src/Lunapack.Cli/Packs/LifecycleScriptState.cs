namespace Lunapack.Cli.Packs;

internal enum LifecycleScriptState
{
    None,
    Ignored,
    Skipped,
    Succeeded,
    Failed,
    Cancelled,
}
