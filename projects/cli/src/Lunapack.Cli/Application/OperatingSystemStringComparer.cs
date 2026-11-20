namespace Lunapack.Cli.Application;

internal static class OperatingSystemStringComparer
{
    public static StringComparer EnvironmentVariableNames => ForCurrentPlatform();

    private static StringComparer ForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
