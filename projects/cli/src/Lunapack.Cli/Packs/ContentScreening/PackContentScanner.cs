using System.Globalization;
using System.Text;
using Lunapack.Cli.Application.Paths;

namespace Lunapack.Cli.Packs.ContentScreening;

internal static class PackContentScanner
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    public static IReadOnlyList<PackContentFinding> Scan(string path, byte[] contents)
    {
        if (contents.AsSpan().IndexOf((byte)0) >= 0)
        {
            return [];
        }

        string text;
        try
        {
            text = _strictUtf8.GetString(contents);
        }
        catch (DecoderFallbackException)
        {
            return [];
        }

        var runes = text.EnumerateRunes().ToArray();
        var findings = new List<PackContentFinding>();
        var normalizedPath = ProjectPath.Normalize(path);
        var line = 1;
        var column = 1;
        var previousWasCarriageReturn = false;
        for (var index = 0; index < runes.Length; index++)
        {
            var rune = runes[index];
            if (rune.Value == '\r')
            {
                line++;
                column = 1;
                previousWasCarriageReturn = true;
                continue;
            }

            if (rune.Value == '\n')
            {
                if (!previousWasCarriageReturn)
                {
                    line++;
                    column = 1;
                }

                previousWasCarriageReturn = false;
                continue;
            }

            previousWasCarriageReturn = false;
            var severity = GetSeverity(runes, index);
            if (severity is { } foundSeverity)
            {
                findings.Add(
                    new PackContentFinding(normalizedPath, line, column, rune.Value, foundSeverity)
                );
            }

            column++;
        }

        return findings;
    }

    private static PackContentFindingSeverity? GetSeverity(Rune[] runes, int index)
    {
        var rune = runes[index];
        if (IsBidiControl(rune.Value) || rune.Value == 0x200B)
        {
            return PackContentFindingSeverity.HighConfidence;
        }

        if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format)
        {
            return null;
        }

        return rune.Value == 0x200D && IsValidEmojiJoiner(runes, index)
            ? null
            : PackContentFindingSeverity.Warning;
    }

    private static bool IsBidiControl(int value) =>
        value is 0x061C or 0x200E or 0x200F or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069;

    private static bool IsValidEmojiJoiner(Rune[] runes, int index)
    {
        var previousIndex = index - 1;
        while (previousIndex >= 0 && EmojiUnicodeData.IsExtend(runes[previousIndex]))
        {
            previousIndex--;
        }

        var nextIndex = index + 1;
        return previousIndex >= 0
            && nextIndex < runes.Length
            && EmojiUnicodeData.IsExtendedPictographic(runes[previousIndex])
            && EmojiUnicodeData.IsExtendedPictographic(runes[nextIndex]);
    }
}
