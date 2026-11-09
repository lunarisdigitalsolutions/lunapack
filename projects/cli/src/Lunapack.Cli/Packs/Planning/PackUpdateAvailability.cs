using Lunapack.Cli.Catalog;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Planning;

internal sealed record PackUpdateAvailability(
    IReadOnlyList<AvailablePackUpdate> Updates,
    IReadOnlyList<OciReleaseIntegrityViolation> IntegrityViolations,
    IReadOnlyList<ProjectLockFile.ResolvedPack> UncheckedOciPacks
);
