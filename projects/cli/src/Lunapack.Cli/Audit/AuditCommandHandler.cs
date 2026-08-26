using System.CommandLine;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class AuditCommandHandler(
    ProjectStateStore projectStateStore,
    LinkLifecycleService linkLifecycleService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    WorkflowPrerequisiteGuard prerequisiteGuard,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("audit", "Report resolved pack and link state.");
        command.SetAction(parseResult =>
            AuditAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                )
            )
        );

        return command;
    }

    public async Task<int> AuditAsync(string projectDirectory)
    {
        var prerequisiteFailure = await prerequisiteGuard.RequireWorkspaceAsync(projectDirectory);
        if (prerequisiteFailure is not null)
        {
            return prerequisiteFailure.Value;
        }

        var state = await projectStateStore.LoadAsync(projectDirectory);
        if (state.Value is not { } projectState)
        {
            return console.Fail(state.Error);
        }

        var table = new Table().Title("[bold]Resolved packs[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Pack[/]");
        table.AddColumn("[bold]Source[/]");
        table.AddColumn("[bold]Dependencies[/]");
        table.AddColumn("[bold]Managed files[/]");
        foreach (
            var pack in projectState.LockFile.Packs.OrderBy(pack => pack.Id, StringComparer.Ordinal)
        )
        {
            table.AddRow(
                Markup.Escape($"{pack.Id}@{pack.Version}"),
                Markup.Escape($"{pack.SourcePath}/{pack.PackPath}"),
                Markup.Escape(
                    pack.Packs.Count == 0
                        ? "-"
                        : string.Join(
                            ", ",
                            pack.Packs.Select(dependency => $"{dependency.Id}@{dependency.Version}")
                        )
                ),
                Markup.Escape(
                    pack.ManagedFiles.Count == 0
                        ? "-"
                        : string.Join(", ", pack.ManagedFiles.Select(file => file.TargetPath))
                )
            );
        }

        console.Render(table);
        RenderLinks(projectDirectory, projectState.LockFile);
        return 0;
    }

    private void RenderLinks(string projectDirectory, ProjectLockFile lockFile)
    {
        var reports = linkLifecycleService.Audit(projectDirectory, lockFile);
        if (reports.Count == 0)
        {
            return;
        }

        var table = new Table().Title("[bold]Resolved links[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Link[/]");
        table.AddColumn("[bold]Source[/]");
        table.AddColumn("[bold]Commit[/]");
        table.AddColumn("[bold]Managed files[/]");
        foreach (var report in reports)
        {
            table.AddRow(
                Markup.Escape(report.Name),
                Markup.Escape(report.SourceName),
                Markup.Escape(report.ResolvedCommit ?? "-"),
                Markup.Escape(
                    report.Files.Count == 0
                        ? "-"
                        : string.Join(
                            ", ",
                            report.Files.Select(file => $"{file.TargetPath} ({file.Status})")
                        )
                )
            );
        }

        console.Render(table);
    }
}
