namespace Lunapack.Cli;

internal sealed record SourceFingerprint
{
    public const string GitType = "git";

    public const string LocalType = "local";

    public required string Identity { get; init; }

    public string? Path { get; init; }

    public string? Ref { get; init; }

    public required string Type { get; init; }

    public string Value =>
        string.Equals(Type, LocalType, StringComparison.Ordinal)
            ? $"{Type}:{Identity}"
            : $"{Type}:{Identity}@{Ref}#{Path}";

    public override string ToString() => Value;
}
