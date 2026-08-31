using System.Globalization;

namespace Lunapack.Cli.Application;

internal static class CliDuration
{
    public static string Format(TimeSpan duration) =>
        $"{duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)}s";
}
