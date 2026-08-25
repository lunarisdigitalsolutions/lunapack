namespace Lunapack.Cli;

internal sealed class ConsoleLifecycleHookConfirmer(CliConsole console) : ILifecycleHookConfirmer
{
    public bool Confirm(ResolvedLifecycleHookInvocation invocation)
    {
        console.Warning(LifecycleHookConfirmationFormatter.Format(invocation));
        return console.IsInteractive && console.Confirm("Run this lifecycle hook?");
    }
}
