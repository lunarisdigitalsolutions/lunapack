namespace Lunapack.Cli;

internal sealed class ConsoleSourceSwitchConfirmer(CliConsole console) : ISourceSwitchConfirmer
{
    public bool Confirm(LockedSourceUpdateSelector.SourceSwitch sourceSwitch) =>
        console.IsInteractive
        && console.Confirm(
            $"Update pack '{sourceSwitch.PackId}' from '{sourceSwitch.CurrentSource}' to '{sourceSwitch.SelectedSource}'?"
        );
}
