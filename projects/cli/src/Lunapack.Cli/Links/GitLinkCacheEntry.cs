namespace Lunapack.Cli.Links;

internal sealed record GitLinkCacheEntry
{
    public required string BlobId { get; init; }

    public required string Path { get; init; }
}
