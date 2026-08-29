namespace Lunapack.Cli.Links;

internal sealed record LinkDefinitionRequest(
    string? Source,
    IReadOnlyList<string> Includes,
    IReadOnlyList<string> Excludes,
    string? Path,
    string? Target,
    string? Ref,
    string? StripPrefix,
    bool Flatten
);
