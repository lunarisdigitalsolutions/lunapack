namespace Lunapack.Cli;

internal sealed record PlannedManagedFile(
    DiscoveredPack Pack,
    string SourcePath,
    string DeclaredTargetPath,
    byte[] Contents,
    string TargetPath,
    string TargetPathRelativeToProject,
    PackManifest.PackManagedFileStrategy Strategy
);
