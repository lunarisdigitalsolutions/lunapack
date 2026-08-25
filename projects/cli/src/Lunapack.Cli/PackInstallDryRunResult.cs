namespace Lunapack.Cli;

internal sealed record PackInstallDryRunResult(
    PackReference SelectedRelease,
    PackUpdatePlan UpdatePlan
);
