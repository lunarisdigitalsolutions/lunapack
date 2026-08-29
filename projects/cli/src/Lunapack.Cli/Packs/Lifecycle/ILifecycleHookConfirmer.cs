namespace Lunapack.Cli.Packs.Lifecycle;

internal interface ILifecycleHookConfirmer
{
    bool Confirm(ResolvedLifecycleHookInvocation invocation);
}
