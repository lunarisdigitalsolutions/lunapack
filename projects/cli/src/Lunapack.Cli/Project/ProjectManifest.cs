namespace Lunapack.Cli.Project;

internal sealed record ProjectManifest
{
    public List<InstalledPack> Packs { get; set; } = [];

    public int SchemaVersion { get; set; }

    public List<LocalSource> Sources { get; set; } = [];

    internal sealed record LocalSource
    {
        public required string Path { get; set; }

        public string Type { get; set; } = "local";
    }

    internal sealed record InstalledPack
    {
        public required string Id { get; set; }

        public List<ManagedFile> ManagedFiles { get; set; } = [];

        public required string SourcePath { get; set; }

        public required string Version { get; set; }
    }

    internal sealed record ManagedFile
    {
        public required string Sha256 { get; set; }

        public required string TargetPath { get; set; }
    }
}
