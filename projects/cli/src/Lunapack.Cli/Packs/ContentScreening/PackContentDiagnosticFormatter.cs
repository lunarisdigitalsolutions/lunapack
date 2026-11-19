using System.Globalization;
using System.Text;
using Lunapack.Cli.Application.Paths;

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
        return $"Hidden Unicode in '{EscapePath(finding.Path)}' at line {finding.Line}, column {finding.Column}: {finding.CodePoint} ({severity}).";
    }

    public static string EscapePath(string path)
    {
        var escaped = new StringBuilder(path.Length);
        foreach (var rune in path.EnumerateRunes())
        {
            if (ProjectPath.IsUnsafePathCharacter(rune))
            {
                escaped
                    .Append("\\u")
                    .Append(rune.Value.ToString("X4", CultureInfo.InvariantCulture));
            }
            else if (rune.Value is '\'' or '\\')
            {
                escaped.Append('\\').Append(rune);
            }
            else
            {
                escaped.Append(rune);
            }
        }

        return escaped.ToString();
    }

    public static string FormatOmitted(long count) =>
        $"{count} additional hidden Unicode finding{(count == 1 ? " was" : "s were")} omitted because the per-payload diagnostic limit was reached.";
}
