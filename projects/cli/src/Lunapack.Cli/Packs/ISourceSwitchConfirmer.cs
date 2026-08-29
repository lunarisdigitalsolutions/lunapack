using Lunapack.Cli.Packs.Planning;

namespace Lunapack.Cli.Packs;

internal interface ISourceSwitchConfirmer
{
    bool Confirm(LockedSourceUpdateSelector.SourceSwitch sourceSwitch);
}
