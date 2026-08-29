namespace Lunapack.Cli.Packs.Planning;

internal sealed record PackInstallDryRunResult(
    PackReference SelectedRelease,
    PackUpdatePlan UpdatePlan
);
