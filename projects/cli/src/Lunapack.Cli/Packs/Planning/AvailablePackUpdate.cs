using Lunapack.Cli.Catalog;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Planning;

internal sealed record AvailablePackUpdate(
    ProjectConfiguration.RequestedPack RequestedRoot,
    ProjectLockFile.ResolvedPack Current,
    CatalogPack Latest,
    string Reason = "pack update"
);
