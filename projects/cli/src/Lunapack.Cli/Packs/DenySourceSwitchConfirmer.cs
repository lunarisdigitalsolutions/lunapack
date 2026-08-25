namespace Lunapack.Cli;

internal sealed class DenySourceSwitchConfirmer : ISourceSwitchConfirmer
{
    public bool Confirm(LockedSourceUpdateSelector.SourceSwitch sourceSwitch) => false;
}
