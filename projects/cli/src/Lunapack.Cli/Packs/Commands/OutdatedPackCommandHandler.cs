using System.CommandLine;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class OutdatedPackCommandHandler(
    PackUpdateSelectionService updateSelectionService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    WorkflowPrerequisiteGuard prerequisiteGuard,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command(
            "outdated",
            "List installed packs with newer available releases."
        );
        command.SetAction(parseResult =>
            OutdatedAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                )
            )
        );

        return command;
    }

    public async Task<int> OutdatedAsync(string projectDirectory)
    {
        var prerequisiteFailure = await prerequisiteGuard.RequireSourcesAsync(projectDirectory);
        if (prerequisiteFailure is not null)
        {
            return prerequisiteFailure.Value;
        }

        var availableUpdates = await updateSelectionService.GetAvailableAsync(projectDirectory);
        if (availableUpdates.Value is not { } updates)
        {
            return console.Fail(availableUpdates.Error);
        }

        if (updates.Count == 0)
        {
            console.Info("No updates are available.");
            return 0;
        }

        var table = new Table().Title("[bold]Available updates[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Pack[/]");
        table.AddColumn("[bold]Current[/]");
        table.AddColumn("[bold]Latest[/]");
        foreach (var update in updates)
        {
            table.AddRow(
                Markup.Escape(update.RequestedRoot.Id),
                Markup.Escape(update.Current.Version),
                Markup.Escape(update.Latest.Manifest.Version)
            );
        }

        console.Render(table);
        return 0;
    }
}
