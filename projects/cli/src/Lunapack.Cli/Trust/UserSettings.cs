namespace Lunapack.Cli.Trust;

internal sealed record UserSettings
{
    public UserTrust Global { get; set; } = new();

    public Dictionary<string, LocalProjectTrust> Projects { get; set; } =
        new(StringComparer.Ordinal);
}
