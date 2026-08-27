using System.CommandLine;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class OutdatedPackCommandHandler(
    PackUpdateSelectionService updateSelectionService,
    LinkLifecycleService linkLifecycleService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    WorkflowPrerequisiteGuard prerequisiteGuard,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command(
            "outdated",
            "List installed packs and links with newer available content."
        );
        var offlineOption = new Option<bool>("--offline")
        {
            Description = "Use lock and cache evidence without contacting remotes.",
        };
        command.Options.Add(offlineOption);
        command.SetAction(parseResult =>
            OutdatedAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(offlineOption)
            )
        );

        return command;
    }

    public async Task<int> OutdatedAsync(string projectDirectory, bool offline = false)
    {
        var prerequisiteFailure = await prerequisiteGuard.RequireSourcesAsync(projectDirectory);
        if (prerequisiteFailure is not null)
        {
            return prerequisiteFailure.Value;
        }

        var availableUpdates = await updateSelectionService.GetAvailableAsync(
            projectDirectory,
            offline
        );
        if (availableUpdates.Value is not { } updates)
        {
            return console.Fail(availableUpdates.Error);
        }

        if (offline)
        {
            console.Info(
                "Remote refs were not checked; results use available lock and cache evidence."
            );
        }

        var reportedLinks = await linkLifecycleService.OutdatedAsync(projectDirectory);
        if (reportedLinks.Value is not { } outdatedLinks)
        {
            return console.Fail(reportedLinks.Error);
        }

        if (updates.Count == 0 && outdatedLinks.Count == 0)
        {
            console.Info("No updates are available.");
            return 0;
        }

        if (updates.Count > 0)
        {
            var table = new Table().Title("[bold]Available updates[/]").Border(TableBorder.Rounded);
            table.AddColumn("[bold]Pack[/]");
            table.AddColumn("[bold]Current[/]");
            table.AddColumn("[bold]Latest[/]");
            table.AddColumn("[bold]Reason[/]");
            foreach (var update in updates)
            {
                table.AddRow(
                    Markup.Escape(update.RequestedRoot.Id),
                    Markup.Escape(update.Current.Version),
                    Markup.Escape(update.Latest.Manifest.Version),
                    Markup.Escape(update.Reason)
                );
            }

            console.Render(table);
        }

        if (outdatedLinks.Count > 0)
        {
            console.Render(CreateOutdatedLinkTable(outdatedLinks));
        }

        return 0;
    }

    private static Table CreateOutdatedLinkTable(IReadOnlyList<LinkOutdatedReport> outdatedLinks)
    {
        var table = new Table().Title("[bold]Outdated links[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Link[/]");
        table.AddColumn("[bold]Reasons[/]");
        foreach (var link in outdatedLinks)
        {
            table.AddRow(Markup.Escape(link.Name), Markup.Escape(string.Join(", ", link.Reasons)));
        }

        return table;
    }
}
