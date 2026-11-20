using Lunapack.Cli.Application;

namespace Lunapack.Cli.Trust;

internal sealed class ConsoleTrustConfirmer(CliConsole console) : ITrustConfirmer
{
    public bool Confirm(string warning)
    {
        if (!console.IsInteractive)
        {
            console.WarningLines(warning);
            return false;
        }

        return console.ConfirmWithDetails("Grant this lifecycle script trust?", [warning]);
    }
}
