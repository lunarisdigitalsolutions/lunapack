using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.Sources.Git;

internal sealed record GitCachedPack
{
    public required string Id { get; init; }

    public required PackManifest Manifest { get; init; }

    public required string PackPath { get; init; }

    public required string Version { get; init; }
}
