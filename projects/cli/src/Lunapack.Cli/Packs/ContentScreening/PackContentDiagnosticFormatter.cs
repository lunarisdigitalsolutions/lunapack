namespace Lunapack.Cli.Packs.ContentScreening;

internal static class PackContentDiagnosticFormatter
{
    public static string Format(PackContentFinding finding)
    {
        var severity = finding.Severity switch
        {
            PackContentFindingSeverity.HighConfidence => "high-confidence",
            _ => "warning",
        };
        return $"Hidden Unicode in '{finding.Path}' at line {finding.Line}, column {finding.Column}: {finding.CodePoint} ({severity}).";
    }
}
