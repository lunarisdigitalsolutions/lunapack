namespace Lunapack.Cli;

internal sealed record GitLinkCacheMetadata
{
    public required string ResolvedCommit { get; init; }

    public required ConfiguredSourceIdentity Source { get; init; }

    public List<GitLinkCacheEntry> Tree { get; init; } = [];

    public int Version { get; init; } = GitLinkCache.CacheVersion;
}
