using System.Globalization;

namespace Lunapack.Cli.Sbom;

internal static class SbomTimestamp
{
    public static string Format(DateTimeOffset timestamp) =>
        timestamp
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
