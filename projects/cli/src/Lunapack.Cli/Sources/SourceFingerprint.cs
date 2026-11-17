namespace Lunapack.Cli.Sources;

internal sealed record SourceFingerprint
{
    public const string GitType = "git";

    public const string LocalType = "local";

    public const string OciType = "oci";

    public required string Identity { get; init; }

    public string? Path { get; init; }

    public string? Ref { get; init; }

    public required string Type { get; init; }

    public string Value =>
        Type switch
        {
            LocalType => $"{Type}:{Identity}",
            OciType => $"{Type}:{Identity}#{Path}",
            _ => $"{Type}:{Identity}@{Ref}#{Path}",
        };

    public override string ToString() => Value;
}
