namespace Lunapack.Cli.Links;

internal sealed record LinkDetail(
    LinkSummary Summary,
    string? EffectiveRef,
    string? ResolvedCommit,
    string BasePath,
    IReadOnlyList<string> Includes,
    IReadOnlyList<string> Excludes,
    bool Flatten,
    string? StripPrefix
);
