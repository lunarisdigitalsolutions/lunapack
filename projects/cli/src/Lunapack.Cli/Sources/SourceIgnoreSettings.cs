namespace Lunapack.Cli.Sources;

internal sealed record SourceIgnoreSettings
{
    public Dictionary<string, List<ConfiguredSourceIdentity>> Projects { get; set; } =
        new(StringComparer.Ordinal);
}
