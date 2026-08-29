namespace Lunapack.Cli.Project;

internal sealed record InitialProjectConfiguration
{
    public List<object> Packs { get; set; } = [];

    public int SchemaVersion { get; set; }

    public List<object> Sources { get; set; } = [];
}
