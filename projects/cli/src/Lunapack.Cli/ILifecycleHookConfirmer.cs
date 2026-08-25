namespace Lunapack.Cli;

internal interface ILifecycleHookConfirmer
{
    bool Confirm(ResolvedLifecycleHookInvocation invocation);
}
