using System.Globalization;

namespace Lunapack.Cli.Packs.ExternalSources;

internal sealed class ConsoleExternalSourceApprover(CliConsole console) : IExternalSourceApprover
{
    public Task<bool> ApproveAsync(
        IReadOnlyList<ExternalSourceRequirementGroup> sources,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var details = sources.Select(source =>
        {
            var descriptions = source
                .Uses.Select(use => use.Description)
                .Where(description => !string.IsNullOrWhiteSpace(description))
                .Distinct(StringComparer.Ordinal);
            var requiredBy = string.Join(
                ", ",
                source.Uses.Select(use => $"{use.PackId}:{use.Alias}")
            );
            return string.Join(
                    " | ",
                    source.WorkspaceSourceName,
                    source.Fingerprint.Identity,
                    source.Fingerprint.Ref,
                    source.Fingerprint.Path,
                    $"required by {requiredBy}",
                    $"{source.FileEntryCount.ToString(CultureInfo.InvariantCulture)} file selector(s)",
                    string.Join("; ", descriptions)
                )
                .TrimEnd(' ', '|');
        });
        var prompt =
            $"Add required external sources?{Environment.NewLine}{string.Join(Environment.NewLine, details)}";
        return Task.FromResult(console.Confirm(prompt));
    }
}
