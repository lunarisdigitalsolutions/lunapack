using Lunapack.Cli.Application;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed class ConsoleLifecycleHookConfirmer(CliConsole console) : ILifecycleHookConfirmer
{
    public bool Confirm(ResolvedLifecycleHookInvocation invocation)
    {
        var details = new List<string>
        {
            string.Empty,
            $"Pack '{invocation.Invocation.Pack.Manifest.Id}' requests permission to run a lifecycle command.",
            LifecycleHookConfirmationFormatter.FormatCommand(invocation),
        };
        if (invocation.Invocation.Script.Description is { } description)
        {
            details.Add(description);
        }

        details.Add(string.Empty);
        console.WriteDetails(details);
        var confirmed = console.IsInteractive && console.Confirm("Run this script?", false);
        if (!confirmed)
        {
            console.Warning(
                $"Lifecycle hook '{LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook)}' for pack '{invocation.Invocation.Pack.Manifest.Id}' was not authorized and will be skipped."
            );
            console.Info(string.Empty);
        }

        return confirmed;
    }
}
