namespace Lunapack.Cli;

internal sealed record WorkspaceGuidance(
    WorkspaceStage Stage,
    int SourceCount,
    int InstalledPackCount,
    IReadOnlyList<NextStepRecommendation> Recommendations
);
