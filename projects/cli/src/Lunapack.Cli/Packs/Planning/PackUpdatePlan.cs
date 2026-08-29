using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.ManagedFiles;

namespace Lunapack.Cli.Packs.Planning;

internal sealed record PackUpdatePlan(
    IReadOnlyList<PlannedPackUpdateAction> Actions,
    LifecycleDryRunPlan? Lifecycle = null,
    ExternalSourceRequirementPlan? ExternalSources = null
);
