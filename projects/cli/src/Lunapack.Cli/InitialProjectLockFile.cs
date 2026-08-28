namespace Lunapack.Cli;

internal sealed record InitialProjectLockFile
{
    public List<object> Packs { get; set; } = [];

    public int SchemaVersion { get; set; }
}
