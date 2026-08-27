namespace Lunapack.Cli;

internal sealed class ConsoleExternalSourceIdentifierPrompter(CliConsole console)
    : IExternalSourceIdentifierPrompter
{
    public Task<string?> PromptAsync(
        ExternalSourceRequirementGroup source,
        string conflictingIdentifier,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        console.Warning(
            $"Source identifier '{conflictingIdentifier}' is already used for another source. Required source: {source.Fingerprint}."
        );
        var value = console.PromptText("Choose another source identifier (empty to cancel):");
        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }
}
