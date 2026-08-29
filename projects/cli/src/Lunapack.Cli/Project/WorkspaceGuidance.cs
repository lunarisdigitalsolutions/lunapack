using Lunapack.Cli.Application.Guidance;

namespace Lunapack.Cli.Project;

internal sealed record WorkspaceGuidance(
    WorkspaceStage Stage,
    int SourceCount,
    int InstalledPackCount,
    IReadOnlyList<NextStepRecommendation> Recommendations
);
