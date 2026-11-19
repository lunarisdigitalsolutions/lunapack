using System.CommandLine;
using Lunapack.Cli.Application;
using Lunapack.Cli.Links;
using Lunapack.Cli.Project;
using Spectre.Console;

namespace Lunapack.Cli.Audit;

internal sealed class AuditCommandHandler(
    AuditService auditService,
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
        var strictOption = new Option<bool>("--strict")
        {
            Description =
                "Fail when a managed target is missing or differs from its locked digest.",
        };
        var failFastOption = new Option<bool>("--fail-fast")
        {
            Description = "Stop strict audit after the first integrity violation.",
        };
        command.Options.Add(strictOption);
        command.Options.Add(failFastOption);
        command.SetAction(parseResult =>
            AuditAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(strictOption),
                parseResult.GetValue(failFastOption)
            )
        );

        return command;
    }

    public async Task<int> AuditAsync(
        string projectDirectory,
        bool strict = false,
        bool failFast = false
    )
    {
        if (failFast && !strict)
        {
            return console.Fail("Option '--fail-fast' requires '--strict'.");
        }

        var prerequisiteFailure = await prerequisiteGuard.RequireWorkspaceAsync(projectDirectory);
        if (prerequisiteFailure is not null)
        {
            return prerequisiteFailure.Value;
        }

        if (strict)
        {
            return await AuditStrictAsync(projectDirectory, failFast);
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

        var state = await projectStateStore.LoadAsync(projectDirectory);
        if (state.Value is not { } projectState)
        {
            return console.Fail(state.Error);
        }

        RenderLinks(projectDirectory, projectState.LockFile);
        return 0;
    }

    private async Task<int> AuditStrictAsync(string projectDirectory, bool failFast)
    {
        var state = await projectStateStore.LoadAsync(projectDirectory);
        if (state.Value is not { } projectState)
        {
            return console.Fail(state.Error);
        }

        var inspected = auditService.InspectManagedFiles(
            projectDirectory,
            projectState.LockFile,
            failFast
        );
        if (inspected.Value is not { } reports)
        {
            return console.Fail(inspected.Error);
        }

        RenderIntegrity(reports);
        var violationCount = reports.Count(report => report.IsViolation);
        if (violationCount == 0)
        {
            return 0;
        }

        console.Error(
            $"Managed file integrity audit found {violationCount} violation{(violationCount == 1 ? string.Empty : "s")}."
        );
        return 2;
    }

    private void RenderIntegrity(IReadOnlyList<ManagedFileIntegrityResult> reports)
    {
        if (reports.Count == 0)
        {
            console.Info("No managed files are recorded in the lock file.");
            return;
        }

        var table = new Table()
            .Title("[bold]Managed file integrity[/]")
            .Border(TableBorder.Rounded);
        table.AddColumn("[bold]Owner[/]");
        table.AddColumn("[bold]Target[/]");
        table.AddColumn("[bold]Status[/]");
        foreach (var report in reports)
        {
            table.AddRow(
                Markup.Escape(AuditOutputFormatter.Format(report.Owner)),
                Markup.Escape(report.TargetPath),
                Markup.Escape(AuditOutputFormatter.Format(report.Status))
            );
        }

        console.Render(table);
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
