namespace Lunapack.Cli;

internal interface IExternalSourceIdentifierPrompter
{
    Task<string?> PromptAsync(
        ExternalSourceRequirementGroup source,
        string conflictingIdentifier,
        CancellationToken cancellationToken
    );
}
