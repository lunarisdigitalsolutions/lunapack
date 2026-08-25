namespace Lunapack.Cli;

internal sealed record AvailablePackUpdate(
    ProjectConfiguration.RequestedPack RequestedRoot,
    ProjectLockFile.ResolvedPack Current,
    CatalogPack Latest
);
