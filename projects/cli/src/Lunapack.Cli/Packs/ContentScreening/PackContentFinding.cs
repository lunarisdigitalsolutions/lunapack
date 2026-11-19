namespace Lunapack.Cli.Packs.ContentScreening;

internal sealed record PackContentFinding(
    string Path,
    int Line,
    int Column,
    int ScalarValue,
    PackContentFindingSeverity Severity
)
{
    public string CodePoint => $"U+{ScalarValue:X4}";
}
