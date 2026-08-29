using System.CommandLine;
using Lunapack.Cli.Application;

namespace Lunapack.Cli.Packs.Commands;

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
        var saveRemapOption = new Option<bool>("--save-remap")
        {
            Description = "Save the move as a target remapping in lunapack.yml.",
        };
        var command = new Command("mv", "Relocate managed files while retaining ownership.")
        {
            sourceArgument,
            targetArgument,
            saveRemapOption,
        };
        command.Aliases.Add("move");
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
                        parseResult.GetValue(targetArgument) ?? string.Empty,
                        parseResult.GetValue(saveRemapOption)
                    )
            );
        });

        return command;
    }
}
