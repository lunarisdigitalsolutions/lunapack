using System.CommandLine;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class AuditCommandHandler(
    AuditService auditService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    WorkflowPrerequisiteGuard prerequisiteGuard,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("audit", "Report resolved pack state.");
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

        var inspected = await auditService.InspectAsync(projectDirectory);
        if (inspected.Value is not { } report)
        {
            return console.Fail(inspected.Error);
        }

        var table = new Table().Title("[bold]Resolved packs[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Pack[/]");
        table.AddColumn("[bold]Source[/]");
        table.AddColumn("[bold]Dependencies[/]");
        table.AddColumn("[bold]Managed files[/]");
        foreach (var pack in report.Packs.OrderBy(pack => pack.Id, StringComparer.Ordinal))
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
        foreach (var source in report.ExternalSources)
        {
            console.Info(AuditOutputFormatter.Format(source));
        }

        foreach (var file in report.ExternalFiles)
        {
            console.Info(AuditOutputFormatter.Format(file));
        }

        return 0;
    }
}
