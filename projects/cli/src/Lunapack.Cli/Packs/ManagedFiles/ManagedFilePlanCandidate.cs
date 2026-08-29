using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record ManagedFilePlanCandidate(
    DiscoveredPack Pack,
    ManagedFileContentRoot ContentRoot,
    string SourcePath,
    string Target,
    string DeclaredTarget,
    PackManifest.PackManagedFileStrategy Strategy,
    bool IsTemplate
);
