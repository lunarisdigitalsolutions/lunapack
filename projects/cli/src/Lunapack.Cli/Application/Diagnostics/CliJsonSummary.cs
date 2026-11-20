using System.Text.Json.Serialization;

namespace Lunapack.Cli.Application.Diagnostics;

internal sealed record CliJsonSummary(
    [property: JsonPropertyName("totalFindings")] int TotalFindings,
    [property: JsonPropertyName("bySeverity")] CliJsonSummary.SeverityCounts BySeverity
)
{
    public static CliJsonSummary FromFindings(IReadOnlyList<CliJsonFinding> findings) =>
        new(
            findings.Count,
            new SeverityCounts(
                findings.Count(finding =>
                    string.Equals(finding.Severity, "info", StringComparison.Ordinal)
                ),
                findings.Count(finding =>
                    string.Equals(finding.Severity, "warning", StringComparison.Ordinal)
                ),
                findings.Count(finding =>
                    string.Equals(finding.Severity, "error", StringComparison.Ordinal)
                )
            )
        );

    internal sealed record SeverityCounts(
        [property: JsonPropertyName("info")] int Info,
        [property: JsonPropertyName("warning")] int Warning,
        [property: JsonPropertyName("error")] int Error
    );
}
