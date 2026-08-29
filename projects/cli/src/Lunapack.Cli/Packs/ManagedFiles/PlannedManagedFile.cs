using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record PlannedManagedFile(
    DiscoveredPack Pack,
    string SourcePath,
    string DeclaredTargetPath,
    byte[] Contents,
    string TargetPath,
    string TargetPathRelativeToProject,
    PackManifest.PackManagedFileStrategy Strategy,
    PlannedExternalSource? ExternalSource = null
);
