using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.ExternalSources;

internal sealed record ApprovedExternalSourcePlan(
    ExternalSourceRequirementPlan Requirements,
    ProjectConfiguration CandidateConfiguration
);
