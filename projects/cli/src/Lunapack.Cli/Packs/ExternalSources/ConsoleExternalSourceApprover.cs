using System.Globalization;
using Lunapack.Cli.Application;

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
        return Task.FromResult(
            console.ConfirmWithDetails("Add required external sources?", details)
        );
    }
}
