namespace Lunapack.Cli;

internal sealed record ProjectLockFile
{
    public Dictionary<string, ResolvedLink> Links { get; set; } = [];

    public List<ResolvedPack> Packs { get; set; } = [];

    public int SchemaVersion { get; set; }

    internal sealed record LinkFile
    {
        public required string DeclaredTargetPath { get; set; }

        public required string Sha256 { get; set; }

        public required string SourcePath { get; set; }

        public required string TargetPath { get; set; }
    }

    internal sealed record ResolvedLink
    {
        public required string DefinitionSha256 { get; set; }

        public List<LinkFile> Files { get; set; } = [];

        public GitSourceProvenance? GitSource { get; set; }

        public required ConfiguredSourceIdentity SourceIdentity { get; set; }

        public required string SourceName { get; set; }
    }

    internal sealed record ManagedFile
    {
        public string? Content { get; set; }

        public string? DeclaredTargetPath { get; set; }

        public required string Sha256 { get; set; }

        public string? SourceAlias { get; set; }

        public string? SourceFingerprint { get; set; }

        public string? SourceName { get; set; }

        public string? SourcePath { get; set; }

        public ManagedFileStrategy? Strategy { get; set; }

        public required string TargetPath { get; set; }
    }

    internal sealed record ExternalSourceLock
    {
        public required string Fingerprint { get; set; }

        public required string Ref { get; set; }

        public required string ResolvedCommit { get; set; }

        public required string SourceName { get; set; }
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

        public Dictionary<string, ExternalSourceLock> ExternalSources { get; set; } = [];

        public GitSourceProvenance? GitSource { get; set; }

        public List<PackReference> Packs { get; set; } = [];

        public required string Id { get; set; }

        public List<ManagedFile> ManagedFiles { get; set; } = [];

        public required string PackPath { get; set; }

        public string? SourcePath { get; set; }

        public required string Version { get; set; }
    }
}
