namespace Lunapack.Cli;

internal interface INextStepAdvisor
{
    Task<ManifestOperationResult<WorkspaceGuidance>> InspectWorkspaceAsync(string projectDirectory);

    IReadOnlyList<NextStepRecommendation> Recommend(NextStepContext context, string? value = null);
}
