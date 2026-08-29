using Lunapack.Cli.Packs.Planning;

namespace Lunapack.Cli.Packs;

internal interface IPackUpdatePrompter
{
    bool Confirm(AvailablePackUpdate update);
}
