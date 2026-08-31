using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Guidance;

namespace Lunapack.Cli.Project;

internal sealed class WorkflowPrerequisiteGuard(
    NextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    CliConsole console
)
{
    public async Task<int?> RequireSourcesAsync(string projectDirectory)
    {
        var inspectedWorkspace = await nextStepAdvisor.InspectWorkspaceAsync(projectDirectory);
        if (inspectedWorkspace.Value is not { } workspace)
        {
            return console.Fail(inspectedWorkspace.Error);
        }

        if (workspace.Stage == WorkspaceStage.NoWorkspace)
        {
            return RenderMissingPrerequisite(missingWorkspace: true);
        }

        if (workspace.SourceCount > 0)
        {
            return null;
        }

        return RenderMissingPrerequisite(missingWorkspace: false);
    }

    public async Task<int?> RequireWorkspaceAsync(string projectDirectory)
    {
        var inspectedWorkspace = await nextStepAdvisor.InspectWorkspaceAsync(projectDirectory);
        if (inspectedWorkspace.Value is not { } workspace)
        {
            return console.Fail(inspectedWorkspace.Error);
        }

        return workspace.Stage == WorkspaceStage.NoWorkspace
            ? RenderMissingPrerequisite(missingWorkspace: true)
            : null;
    }

    private int RenderMissingPrerequisite(bool missingWorkspace)
    {
        var exitCode = console.Fail(
            missingWorkspace ? "No LunaPack workspace found." : "No sources are configured."
        );
        nextStepRenderer.Render(
            nextStepAdvisor.Recommend(
                missingWorkspace ? NextStepContext.MissingWorkspace : NextStepContext.MissingSources
            )
        );
        return exitCode;
    }
}
