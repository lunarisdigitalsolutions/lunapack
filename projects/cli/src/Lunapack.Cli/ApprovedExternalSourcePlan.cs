namespace Lunapack.Cli;

internal sealed record ApprovedExternalSourcePlan(
    ExternalSourceRequirementPlan Requirements,
    ProjectConfiguration CandidateConfiguration
);
