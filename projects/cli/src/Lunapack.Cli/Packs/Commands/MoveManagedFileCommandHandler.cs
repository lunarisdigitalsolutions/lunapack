using System.CommandLine;

namespace Lunapack.Cli;

internal sealed class MoveManagedFileCommandHandler(
    PackLifecycleService packLifecycleService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var sourceArgument = new Argument<string>("source")
        {
            Description = "Current project-relative managed-file target.",
        };
        var targetArgument = new Argument<string>("target")
        {
            Description = "New project-relative managed-file target.",
        };
        var command = new Command("mv", "Relocate a managed file while retaining ownership.")
        {
            sourceArgument,
            targetArgument,
        };
        command.SetAction(async parseResult =>
        {
            var workspaceDirectory = workspaceDirectoryResolver.Resolve(
                projectDirectory,
                parseResult.GetValue(workspaceOption)
            );
            return await console.RunWithStatusAsync(
                "Moving managed file...",
                () =>
                    packLifecycleService.MoveManagedFileAsync(
                        workspaceDirectory,
                        parseResult.GetValue(sourceArgument) ?? string.Empty,
                        parseResult.GetValue(targetArgument) ?? string.Empty
                    )
            );
        });

        return command;
    }
}
