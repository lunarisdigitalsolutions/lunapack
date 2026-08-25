namespace Lunapack.Cli;

internal sealed record PreparedPackUpdate(
    ProjectState State,
    ProjectConfiguration Configuration,
    ResolvedPackGraph Graph,
    PackInstallationPlan InstallationPlan,
    PackUpdatePlan UpdatePlan,
    ResolvedPackParameters Parameters,
    GitPackMaterialization Materialization
);
