namespace Lunapack.Cli;

internal interface IPackUpdatePrompter
{
    bool Confirm(AvailablePackUpdate update);
}
