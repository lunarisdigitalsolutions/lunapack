namespace Lunapack.Cli.Packs.ExternalSources;

internal sealed class DenyExternalSourceIdentifierPrompter : IExternalSourceIdentifierPrompter
{
    public Task<string?> PromptAsync(
        ExternalSourceRequirementGroup source,
        string conflictingIdentifier,
        CancellationToken cancellationToken
    ) => Task.FromResult<string?>(null);
}
