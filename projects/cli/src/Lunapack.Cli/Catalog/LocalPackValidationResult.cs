using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.Catalog;

internal sealed record LocalPackValidationResult(
    string ManifestPath,
    PackManifest? Manifest,
    IReadOnlyList<string> Issues
)
{
    public bool IsValid => Manifest is not null && Issues.Count == 0;
}
