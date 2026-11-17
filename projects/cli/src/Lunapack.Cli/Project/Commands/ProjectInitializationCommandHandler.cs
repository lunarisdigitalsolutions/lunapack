using System.CommandLine;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Guidance;

namespace Lunapack.Cli.Project.Commands;

internal sealed class ProjectInitializationCommandHandler(
    IFileSystem fileSystem,
    ProjectStateStore projectStateStore,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    NextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    CliConsole console
)
{
    private const string ProjectStateIgnorePattern = "/.lunapack/";
    private static readonly char[] _lineEndings = ['\r', '\n'];

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

        if (!EnsureProjectStateIgnored(projectDirectory))
        {
            return 1;
        }

        var result = await projectStateStore.InitializeAsync(projectDirectory);

        if (!result.IsSuccess)
        {
            return console.Fail(result.Error);
        }

        console.Success("Workspace initialized");
        nextStepRenderer.Render(nextStepAdvisor.Recommend(NextStepContext.WorkspaceInitialized));
        return 0;
    }

    private bool EnsureProjectStateIgnored(string projectDirectory)
    {
        var gitIgnorePath = fileSystem.Path.Combine(projectDirectory, ".gitignore");
        try
        {
            if (!fileSystem.File.Exists(gitIgnorePath))
            {
                fileSystem.File.WriteAllText(
                    gitIgnorePath,
                    $"{ProjectStateIgnorePattern}{Environment.NewLine}"
                );
                return true;
            }

            var contents = fileSystem.File.ReadAllText(gitIgnorePath);
            if (
                contents
                    .Split(_lineEndings, StringSplitOptions.RemoveEmptyEntries)
                    .Any(line =>
                        string.Equals(
                            line.Trim(),
                            ProjectStateIgnorePattern,
                            StringComparison.Ordinal
                        )
                    )
            )
            {
                return true;
            }

            var separator =
                contents.Length == 0 || contents.EndsWith('\n') || contents.EndsWith('\r')
                    ? string.Empty
                    : Environment.NewLine;
            fileSystem.File.AppendAllText(
                gitIgnorePath,
                $"{separator}{ProjectStateIgnorePattern}{Environment.NewLine}"
            );
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            console.Fail($"Unable to update project .gitignore: {exception.Message}");
            return false;
        }
    }
}
