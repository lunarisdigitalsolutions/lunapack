namespace Lunapack.Cli;

internal sealed record PackUpdatePlan(
    IReadOnlyList<PlannedPackUpdateAction> Actions,
    LifecycleDryRunPlan? Lifecycle = null,
    ExternalSourceRequirementPlan? ExternalSources = null
);
