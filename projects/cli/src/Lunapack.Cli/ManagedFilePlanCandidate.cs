namespace Lunapack.Cli;

internal sealed record ManagedFilePlanCandidate(
    DiscoveredPack Pack,
    ManagedFileContentRoot ContentRoot,
    string SourcePath,
    string Target,
    string DeclaredTarget,
    PackManifest.PackManagedFileStrategy Strategy,
    bool IsTemplate
);
