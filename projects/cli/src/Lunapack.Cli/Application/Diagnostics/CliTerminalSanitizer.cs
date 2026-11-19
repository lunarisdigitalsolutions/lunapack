using System.Globalization;
using System.Text;

namespace Lunapack.Cli.Application.Diagnostics;

internal static class CliTerminalSanitizer
{
    public static string Sanitize(string value)
    {
        var firstControl = -1;
        for (var index = 0; index < value.Length; index++)
        {
            if (IsTerminalControl(value[index]))
            {
                firstControl = index;
                break;
            }
        }

        if (firstControl < 0)
        {
            return value;
        }

        var sanitized = new StringBuilder(value.Length + 16);
        sanitized.Append(value, 0, firstControl);
        for (var index = firstControl; index < value.Length; index++)
        {
            var character = value[index];
            if (IsTerminalControl(character))
            {
                AppendEscaped(sanitized, character);
                continue;
            }

            sanitized.Append(character);
        }

        return sanitized.ToString();
    }

    public static string SanitizeWriterChunk(string value)
    {
        var trailingLineEndingStart = value.Length;
        while (trailingLineEndingStart > 0 && value[trailingLineEndingStart - 1] is '\r' or '\n')
        {
            trailingLineEndingStart--;
        }

        var content = Sanitize(value[..trailingLineEndingStart]);
        return trailingLineEndingStart == value.Length
            ? content
            : string.Concat(content, value.AsSpan(trailingLineEndingStart));
    }

    private static bool IsTerminalControl(char character) =>
        char.IsControl(character)
        || char.GetUnicodeCategory(character)
            is UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator;

    private static void AppendEscaped(StringBuilder builder, char character)
    {
        builder.Append("\\u");
        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
    }
}
