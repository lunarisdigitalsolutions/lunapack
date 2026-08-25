namespace Lunapack.Cli;

internal sealed class ConsoleTrustConfirmer(CliConsole console) : ITrustConfirmer
{
    public bool Confirm(string warning)
    {
        console.Warning(warning);
        return console.IsInteractive && console.Confirm("Grant this lifecycle script trust?");
    }
}
