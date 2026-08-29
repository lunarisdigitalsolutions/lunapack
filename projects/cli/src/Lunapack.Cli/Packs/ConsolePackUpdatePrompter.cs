using Lunapack.Cli.Packs.Planning;

namespace Lunapack.Cli.Packs;

internal sealed class ConsolePackUpdatePrompter(CliConsole console) : IPackUpdatePrompter
{
    public bool Confirm(AvailablePackUpdate update) =>
        console.Confirm(
            $"Update {update.RequestedRoot.Id} {update.Current.Version} -> {update.Latest.Manifest.Version}?"
        );
}
