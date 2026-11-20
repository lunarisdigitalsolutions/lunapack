using System.Globalization;
using System.Text;

namespace Lunapack.Cli.Doctor;

internal static class DoctorText
{
    private const int MaximumCharacters = 512;

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var output = new StringBuilder(Math.Min(value.Length, MaximumCharacters));
        var wasTruncated = false;
        foreach (var character in value)
        {
            if (output.Length >= MaximumCharacters)
            {
                wasTruncated = true;
                break;
            }

            var category = char.GetUnicodeCategory(character);
            if (
                char.IsControl(character)
                || category
                    is UnicodeCategory.Format
                        or UnicodeCategory.LineSeparator
                        or UnicodeCategory.ParagraphSeparator
                        or UnicodeCategory.Surrogate
            )
            {
                output.Append(@"\u");
                output.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                output.Append(character);
            }
        }

        if (wasTruncated)
        {
            output.Append("...");
        }

        return output.ToString();
    }
}
