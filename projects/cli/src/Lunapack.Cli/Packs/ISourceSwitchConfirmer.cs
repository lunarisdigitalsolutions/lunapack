namespace Lunapack.Cli;

internal interface ISourceSwitchConfirmer
{
    bool Confirm(LockedSourceUpdateSelector.SourceSwitch sourceSwitch);
}
