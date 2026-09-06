namespace Lunapack.Cli.Project;

internal sealed record InitialProjectLockFile
{
    public List<object> Instances { get; set; } = [];

    public List<object> Packs { get; set; } = [];

    public int SchemaVersion { get; set; }
}
