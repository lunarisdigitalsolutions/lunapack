namespace Lunapack.Cli.Application.Preferences;

internal sealed record CliPreferences
{
    public CatalogPreferences? Discover { get; set; }

    public GitPreferences? Git { get; set; }

    public CatalogPreferences? Search { get; set; }

    public NextStepPreferences? NextSteps { get; set; }
}
