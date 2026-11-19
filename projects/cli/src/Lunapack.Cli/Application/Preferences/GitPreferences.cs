namespace Lunapack.Cli.Application.Preferences;

internal sealed record GitPreferences
{
    public bool? AllowInsecureTransport { get; set; }
}
