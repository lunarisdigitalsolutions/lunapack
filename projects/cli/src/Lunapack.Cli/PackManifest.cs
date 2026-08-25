namespace Lunapack.Cli;

internal sealed record PackManifest
{
    public string? Author { get; set; }

    public string? Description { get; set; }

    public required string Id { get; set; }

    public string? License { get; set; }

    public List<PackManagedFile> ManagedFiles { get; set; } = [];

    public List<PackReference> Packs { get; set; } = [];

    public Dictionary<string, PackParameter> Parameters { get; set; } = [];

    public PackScripts? Scripts { get; set; }

    public List<string> Tags { get; set; } = [];

    public required string Version { get; set; }

    internal sealed record PackManagedFile
    {
        public string? Condition { get; set; }

        public string? Directory { get; set; }

        public string? Glob { get; set; }

        public string? Source { get; set; }

        public PackManagedFileStrategy Strategy { get; set; } =
            PackManagedFileStrategy.CopyOverwrite;

        public bool Template { get; set; }

        public required string Target { get; set; }
    }

    internal sealed record PackManagedFileStrategy
    {
        public static PackManagedFileStrategy CopyOverwrite { get; } = new();

        public string Method { get; set; } = "overwrite";

        public string Type { get; set; } = "copy";
    }

    internal sealed record PackParameter
    {
        public string? Description { get; set; }

        public string? DisplayName { get; set; }

        public bool Required { get; set; }

        public required string Type { get; set; }

        public List<string>? Values { get; set; }
    }

    internal sealed record PackScripts
    {
        public LifecycleScript? PostInstall { get; set; }

        public LifecycleScript? PostUpdate { get; set; }

        public LifecycleScript? PreInstall { get; set; }

        public LifecycleScript? PreUpdate { get; set; }
    }

    internal sealed record LifecycleScript
    {
        public List<string> Arguments { get; set; } = [];

        public string? Command { get; set; }

        public string? Description { get; set; }

        public string? File { get; set; }

        public string? Runner { get; set; }
    }

    internal sealed record PackReference
    {
        public List<string> DisabledHooks { get; set; } = [];

        public required string Id { get; set; }

        public Dictionary<string, object> Parameters { get; set; } = [];

        public required string Version { get; set; }
    }
}
