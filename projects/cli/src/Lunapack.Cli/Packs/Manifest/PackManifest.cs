using YamlDotNet.Serialization;

namespace Lunapack.Cli.Packs.Manifest;

internal sealed record PackManifest
{
    public string? Author { get; set; }

    public string? Description { get; set; }

    public bool Draft { get; set; }

    public string? Homepage { get; set; }

    public PackHooks? Hooks { get; set; }

    public required string Id { get; set; }

    public string? License { get; set; }

    [YamlMember(Alias = "scripts")]
    public Dictionary<string, PackHook>? LegacyScripts { get; set; }

    public List<PackManagedFile> ManagedFiles { get; set; } = [];

    public string? Name { get; set; }

    public List<PackReference> Packs { get; set; } = [];

    public Dictionary<string, PackParameter> Parameters { get; set; } = [];

    public Dictionary<string, PackSource> Sources { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public required string Version { get; set; }

    internal sealed record PackManagedFile
    {
        public string? Condition { get; set; }

        public string? Directory { get; set; }

        public List<string> Exclude { get; set; } = [];

        public bool Flatten { get; set; }

        public string? Glob { get; set; }

        public string? Path { get; set; }

        public string? Source { get; set; }

        public PackManagedFileStrategy Strategy { get; set; } =
            PackManagedFileStrategy.CopyOverwrite;

        public bool Template { get; set; }

        public required string Target { get; set; }
    }

    internal sealed record PackSource
    {
        public string? Description { get; set; }

        public string? Path { get; set; }

        public required string Ref { get; set; }

        public string Type { get; set; } = "git";

        public required string Url { get; set; }
    }

    internal sealed record PackManagedFileStrategy
    {
        public static PackManagedFileStrategy CopyOverwrite { get; } = new();

        public string Method { get; set; } = "overwrite";

        public string Type { get; set; } = "copy";
    }

    internal sealed record PackParameter
    {
        public object? Default { get; set; }

        public string? Description { get; set; }

        public string? DisplayName { get; set; }

        public bool? Multiple { get; set; }

        public bool? Required { get; set; }

        public string? RequiredWhen { get; set; }

        public required string Type { get; set; }

        public List<string>? Values { get; set; }
    }

    internal sealed record PackHooks
    {
        public List<PackHook>? PostInstall { get; set; }

        public List<PackHook>? PostUninstall { get; set; }

        public List<PackHook>? PostUpdate { get; set; }

        public List<PackHook>? PreInstall { get; set; }

        public List<PackHook>? PreUninstall { get; set; }

        public List<PackHook>? PreUpdate { get; set; }
    }

    internal sealed record PackHook
    {
        public List<string> Arguments { get; set; } = [];

        public string? Command { get; set; }

        public string? Condition { get; set; }

        public string? Description { get; set; }

        public string? File { get; set; }

        public string? Runner { get; set; }

        public bool? Templating { get; set; }

        public required string Type { get; set; }
    }

    internal sealed record PackReference
    {
        public string? Condition { get; set; }

        public List<string> DisabledHooks { get; set; } = [];

        public required string Id { get; set; }

        public Dictionary<string, object> Parameters { get; set; } = [];

        public required string Version { get; set; }
    }
}
