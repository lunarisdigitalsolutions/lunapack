using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.Catalog;

internal sealed record LocalPackValidationResult(
    string ManifestPath,
    PackManifest? Manifest,
    IReadOnlyList<string> Issues,
    long ManifestByteCount = 0
)
{
    public bool IsValid => Manifest is not null && Issues.Count == 0;
}
