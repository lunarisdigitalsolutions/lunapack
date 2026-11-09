using Lunapack.Cli.Catalog;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Planning;

internal sealed record OciReleaseIntegrityViolation(
    ProjectLockFile.ResolvedPack LockedPack,
    CatalogPack? ObservedPack
)
{
    public string Message
    {
        get
        {
            var locked = LockedPack.OciSource!;
            var sourceName = LockedPack.SourceName ?? ObservedPack?.SourceName ?? "unknown";
            var observedDigest = ObservedPack?.OciSource?.ManifestDigest ?? "missing";
            return $"OCI release integrity violation for pack '{LockedPack.Id}@{LockedPack.Version}' from source '{sourceName}': manifest digest changed from '{locked.ManifestDigest}' to '{observedDigest}'. Publish a new version instead of replacing an existing release.";
        }
    }
}
