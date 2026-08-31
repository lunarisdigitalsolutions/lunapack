using Lunapack.Cli.Application;
using Spectre.Console;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed class ConsoleLifecycleHookConfirmer(CliConsole console) : ILifecycleHookConfirmer
{
    public bool Confirm(ResolvedLifecycleHookInvocation invocation)
    {
        console.Info(string.Empty);
        console.Accent(
            $"Pack '{invocation.Invocation.Pack.Manifest.Id}' requests permission to run a lifecycle command."
        );
        console.Render(
            new Markup(
                $"[yellow]{Markup.Escape(LifecycleHookConfirmationFormatter.FormatCommand(invocation))}[/]\n"
            )
        );
        if (invocation.Invocation.Script.Description is { } description)
        {
            console.Info(description);
        }

        console.Info(string.Empty);
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
