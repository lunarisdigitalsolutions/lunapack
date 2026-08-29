namespace Lunapack.Cli.Sources.Git;

internal sealed record GitSourceCacheEntry
{
    public string? DefaultBranch { get; init; }

    public List<GitCachedPack> Packs { get; init; } = [];

    public required string ResolvedCommit { get; init; }

    public required GitSourceCacheIdentity Source { get; init; }

    public int Version { get; init; } = GitSourceCache.CacheVersion;
}
