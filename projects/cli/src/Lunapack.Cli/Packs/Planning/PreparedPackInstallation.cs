using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Packs.Planning;

internal sealed record PreparedPackInstallation(
    ProjectState State,
    ProjectConfiguration Configuration,
    ResolvedPackGraph Graph,
    PackInstallationPlan InstallationPlan,
    PackUpdatePlan UpdatePlan,
    ResolvedPackParameters Parameters,
    PackReference SelectedRelease,
    GitPackMaterialization Materialization,
    ExternalSourceMaterialization ExternalMaterialization,
    ExternalSourceRequirementPlan ExternalSources
);
