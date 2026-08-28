namespace Lunapack.Cli;

internal sealed record InitialPackManifest
{
    public string? Author { get; set; }

    public required string Id { get; set; }

    public string? License { get; set; }

    public required string Version { get; set; }
}
