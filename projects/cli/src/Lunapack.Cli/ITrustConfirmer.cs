namespace Lunapack.Cli;

internal interface ITrustConfirmer
{
    bool Confirm(string warning);
}
