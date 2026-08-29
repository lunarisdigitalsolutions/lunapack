using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.Packs.Planning;

internal sealed record ResolvedPackGraph(
    IReadOnlyList<DiscoveredPack> Packs,
    IReadOnlySet<string>? RootPackIds = null
)
{
    public IReadOnlyList<PackManifest.PackReference> GetIncomingReferences(DiscoveredPack pack) =>
        [
            .. Packs
                .SelectMany(parent => parent.Manifest.Packs)
                .Where(reference =>
                    string.Equals(reference.Id, pack.Manifest.Id, StringComparison.Ordinal)
                    && string.Equals(
                        reference.Version,
                        pack.Manifest.Version,
                        StringComparison.Ordinal
                    )
                ),
        ];

    public bool IsRoot(DiscoveredPack pack) =>
        RootPackIds?.Contains(pack.Manifest.Id) is true
        || (
            RootPackIds is null
            && string.Equals(pack.Manifest.Id, Packs[^1].Manifest.Id, StringComparison.Ordinal)
        );
}
