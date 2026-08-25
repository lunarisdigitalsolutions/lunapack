namespace Lunapack.Cli;

internal sealed record ProjectLockFile
{
    public List<ResolvedPack> Packs { get; set; } = [];

    public int SchemaVersion { get; set; }

    internal sealed record ManagedFile
    {
        public string? Content { get; set; }

        public string? DeclaredTargetPath { get; set; }

        public required string Sha256 { get; set; }

        public ManagedFileStrategy? Strategy { get; set; }

        public required string TargetPath { get; set; }
    }

    internal sealed record ManagedFileStrategy
    {
        public required string Method { get; set; }

        public required string Type { get; set; }
    }

    internal sealed record PackReference
    {
        public required string Id { get; set; }

        public required string Version { get; set; }
    }

    internal sealed record ResolvedPack
    {
        public ConfiguredSourceIdentity? SourceIdentity { get; set; }

        public string? SourceName { get; set; }

        public string? Destination { get; set; }

        public GitSourceProvenance? GitSource { get; set; }

        public List<PackReference> Packs { get; set; } = [];

        public required string Id { get; set; }

        public List<ManagedFile> ManagedFiles { get; set; } = [];

        public required string PackPath { get; set; }

        public string? SourcePath { get; set; }

        public required string Version { get; set; }
    }
}
