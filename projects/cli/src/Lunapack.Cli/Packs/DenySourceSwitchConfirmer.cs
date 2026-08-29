using Lunapack.Cli.Packs.Planning;

namespace Lunapack.Cli.Packs;

internal sealed class DenySourceSwitchConfirmer : ISourceSwitchConfirmer
{
    public bool Confirm(LockedSourceUpdateSelector.SourceSwitch sourceSwitch) => false;
}
