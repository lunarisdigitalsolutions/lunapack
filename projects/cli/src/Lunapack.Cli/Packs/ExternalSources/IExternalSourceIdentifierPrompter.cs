namespace Lunapack.Cli.Packs.ExternalSources;

internal interface IExternalSourceIdentifierPrompter
{
    Task<string?> PromptAsync(
        ExternalSourceRequirementGroup source,
        string conflictingIdentifier,
        CancellationToken cancellationToken
    );
}
