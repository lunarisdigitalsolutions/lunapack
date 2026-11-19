using System.Buffers;
using System.Globalization;
using System.Text;

namespace Lunapack.Cli.Application;

internal static class TerminalText
{
    public static string EncodeControls(string value)
    {
        StringBuilder? output = null;
        for (var index = 0; index < value.Length; index++)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var consumed);
            if (status == OperationStatus.Done && !IsUnsafe(rune))
            {
                output?.Append(value, index, consumed);

                index += consumed - 1;
                continue;
            }

            output ??= new StringBuilder(value.Length + 8).Append(value, 0, index);
            if (status == OperationStatus.Done)
            {
                AppendEscape(output, rune);
                index += consumed - 1;
            }
            else
            {
                AppendEscape(output, value[index]);
            }
        }

        return output?.ToString() ?? value;
    }

    private static bool IsUnsafe(Rune rune) =>
        Rune.GetUnicodeCategory(rune)
            is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.Surrogate;

    private static void AppendEscape(StringBuilder output, Rune rune)
    {
        if (rune.IsBmp)
        {
            output.Append("\\u");
            output.Append(rune.Value.ToString("x4", CultureInfo.InvariantCulture));
            return;
        }

        output.Append("\\U");
        output.Append(rune.Value.ToString("x8", CultureInfo.InvariantCulture));
    }

    private static void AppendEscape(StringBuilder output, char value)
    {
        output.Append("\\u");
        output.Append(((int)value).ToString("x4", CultureInfo.InvariantCulture));
    }
}
