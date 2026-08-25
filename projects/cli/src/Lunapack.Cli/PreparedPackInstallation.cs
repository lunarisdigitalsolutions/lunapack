namespace Lunapack.Cli;

internal sealed record PreparedPackInstallation(
    ProjectState State,
    ProjectConfiguration Configuration,
    ResolvedPackGraph Graph,
    PackInstallationPlan InstallationPlan,
    PackUpdatePlan UpdatePlan,
    ResolvedPackParameters Parameters,
    PackReference SelectedRelease,
    GitPackMaterialization Materialization
);
