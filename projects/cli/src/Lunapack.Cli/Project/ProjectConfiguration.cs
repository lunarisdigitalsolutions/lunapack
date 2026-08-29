using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Project;

internal sealed record ProjectConfiguration
{
    public Dictionary<string, Link> Links { get; set; } = [];

    public List<RequestedPack> Packs { get; set; } = [];

    public Remapping? Remap { get; set; }

    public int SchemaVersion { get; set; }

    public List<Source> Sources { get; set; } = [];

    public ProjectTrust Trust { get; set; } = new();

    public Dictionary<string, object> Variables { get; set; } = [];

    internal record Source
    {
        public required string Name { get; set; }
    }

    internal sealed record Link
    {
        public List<string> Excludes { get; set; } = [];

        public bool? Flatten { get; set; }

        public List<string> Includes { get; set; } = [];

        public string? Path { get; set; }

        public string? Ref { get; set; }

        public required string Source { get; set; }

        public string? StripPrefix { get; set; }

        public string? Target { get; set; }
    }

    internal sealed record GitSource : Source
    {
        public string? Path { get; set; }

        public string? Ref { get; set; }

        public int? TimeoutSeconds { get; set; }

        public string Type { get; set; } = "git";

        public required string Url { get; set; }
    }

    internal sealed record LocalSource : Source
    {
        public required string Path { get; set; }

        public string Type { get; set; } = "local";
    }

    internal sealed record Remapping
    {
        public Dictionary<string, string> Directories { get; set; } = [];

        public Dictionary<string, string> Files { get; set; } = [];
    }

    internal sealed record ProjectTrust
    {
        public ScriptDenial? Deny { get; set; }

        public List<TrustedPack> Packs { get; set; } = [];

        public List<string> Sources { get; set; } = [];
    }

    internal sealed record RequestedPack
    {
        public string? Destination { get; set; }

        public required string Id { get; set; }

        public string? Version { get; set; }
    }

    internal sealed record TrustedPack
    {
        public required string Id { get; set; }

        public required string Source { get; set; }
    }
}
