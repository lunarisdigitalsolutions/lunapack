namespace Lunapack.Cli;

internal sealed class ConsoleLifecycleHookConfirmer(CliConsole console) : ILifecycleHookConfirmer
{
    public bool Confirm(ResolvedLifecycleHookInvocation invocation)
    {
        console.Warning(LifecycleHookConfirmationFormatter.Format(invocation));
        var confirmed = console.IsInteractive && console.Confirm("Run this lifecycle hook?");
        if (!confirmed)
        {
            console.Warning(
                $"Lifecycle hook '{LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook)}' for pack '{invocation.Invocation.Pack.Manifest.Id}' was not authorized and will be skipped."
            );
        }

        return confirmed;
    }
}
