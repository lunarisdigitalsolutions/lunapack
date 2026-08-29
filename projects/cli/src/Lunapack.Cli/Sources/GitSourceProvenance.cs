namespace Lunapack.Cli.Sources;

internal sealed record GitSourceProvenance
{
    public string? Path { get; set; }

    public string? Ref { get; set; }

    public required string ResolvedCommit { get; set; }

    public string Type { get; set; } = "git";

    public required string Url { get; set; }
}
