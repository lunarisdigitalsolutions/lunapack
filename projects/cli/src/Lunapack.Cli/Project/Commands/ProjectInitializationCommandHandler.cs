using System.CommandLine;
using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed class ProjectInitializationCommandHandler(
    IFileSystem fileSystem,
    ProjectStateStore projectStateStore,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    INextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("init", "Create a LunaPack project manifest.");
        command.SetAction(parseResult =>
            InitializeAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                )
            )
        );

        return command;
    }

    public async Task<int> InitializeAsync(string projectDirectory)
    {
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        var lockFilePath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.LockFileName
        );
        if (fileSystem.File.Exists(configurationPath) || fileSystem.File.Exists(lockFilePath))
        {
            return console.Fail("Project state already exists.");
        }

        var result = await projectStateStore.SaveAsync(
            projectDirectory,
            new ProjectState
            {
                Configuration = new ProjectConfiguration { SchemaVersion = 1, Variables = [] },
                LockFile = new ProjectLockFile { SchemaVersion = 1 },
            }
        );

        if (!result.IsSuccess)
        {
            return console.Fail(result.Error);
        }

        console.Success("✓ Workspace initialized");
        nextStepRenderer.Render(nextStepAdvisor.Recommend(NextStepContext.WorkspaceInitialized));
        return 0;
    }
}
