namespace Lunapack.Cli.Trust;

internal interface ITrustConfirmer
{
    bool Confirm(string warning);
}
