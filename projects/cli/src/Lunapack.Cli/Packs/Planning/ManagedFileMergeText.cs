using System.Text;

namespace Lunapack.Cli.Packs.Planning;

internal static class ManagedFileMergeText
{
    private static readonly UTF8Encoding _utf8 = new(false, true);

    public static byte[] CreateContents(IReadOnlyList<string> lines, bool trailingNewline)
    {
        var contents = string.Join("\n", lines);
        if (trailingNewline && contents.Length > 0)
        {
            contents += '\n';
        }

        return _utf8.GetBytes(contents);
    }

    public static string Decode(byte[] contents) => _utf8.GetString(contents);

    public static bool HasTrailingNewline(string contents) =>
        contents.EndsWith('\n') || contents.EndsWith('\r');

    public static List<string> ReadLines(string contents)
    {
        var normalized = contents
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
