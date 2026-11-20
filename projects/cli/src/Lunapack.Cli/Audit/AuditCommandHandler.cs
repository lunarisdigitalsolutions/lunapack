using System.CommandLine;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Diagnostics;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.ContentScreening;
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
        var formatOption = CliOutputFormatOption.Create();
        command.Options.Add(strictOption);
        command.Options.Add(failFastOption);
        command.Options.Add(formatOption);
        command.SetAction(async parseResult =>
        {
            var formatValue = parseResult.GetValue(formatOption);
            if (!CliOutputFormatOption.TryParse(formatValue, out var format))
            {
                return console.Fail(CliOutputFormatOption.FormatUnsupportedMessage(formatValue));
            }

            return await AuditAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(strictOption),
                parseResult.GetValue(failFastOption),
                format,
                parseResult.InvocationConfiguration.Output
            );
        });

        return command;
    }

    public async Task<int> AuditAsync(
        string projectDirectory,
        bool strict,
        bool failFast,
        CliOutputFormat format,
        TextWriter output
    )
    {
        if (failFast && !strict)
        {
            var exitCode = console.Fail("Option '--fail-fast' requires '--strict'.");
            WriteJsonFailure(format, output);
            return exitCode;
        }

        if (strict && format == CliOutputFormat.Json)
        {
            var exitCode = console.Fail("Option '--strict' cannot be used with '--format json'.");
            WriteJsonFailure(format, output);
            return exitCode;
        }

        var prerequisiteFailure = await prerequisiteGuard.RequireWorkspaceAsync(projectDirectory);
        if (prerequisiteFailure is not null)
        {
            WriteJsonFailure(format, output);
            return prerequisiteFailure.Value;
        }

        if (strict)
        {
            return await AuditStrictAsync(projectDirectory, failFast);
        }

        var inspected = await auditService.InspectAsync(projectDirectory);
        if (inspected.Value is not { } report)
        {
            return Fail(inspected.Error, format, output);
        }

        if (format == CliOutputFormat.Json)
        {
            var loadedState = await projectStateStore.LoadAsync(projectDirectory);
            if (loadedState.Value is not { } state)
            {
                return Fail(loadedState.Error, format, output);
            }

            var links = linkLifecycleService.Audit(projectDirectory, state.LockFile);
            var findings = AuditJsonMapper.MapFindings(report, links);
            CliJsonWriter.WriteAudit(
                output,
                AuditJsonMapper.MapResult(report, links),
                findings,
                CliJsonSummary.FromFindings(findings)
            );
            return 0;
        }

        RenderReport(report);
        var textState = await projectStateStore.LoadAsync(projectDirectory);
        if (textState.Value is not { } projectState)
        {
            return console.Fail(textState.Error);
        }

        RenderLinks(linkLifecycleService.Audit(projectDirectory, projectState.LockFile));
        return 0;
    }

    private int Fail(string? message, CliOutputFormat format, TextWriter output)
    {
        var exitCode = console.Fail(message);
        WriteJsonFailure(format, output);
        return exitCode;
    }

    private void RenderReport(AuditReport report)
    {
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

        foreach (var finding in report.HiddenCharacterFindings)
        {
            console.Warning(PackContentDiagnosticFormatter.Format(finding));
        }

        if (report.OmittedHiddenCharacterFindingCount > 0)
        {
            console.Warning(
                PackContentDiagnosticFormatter.FormatOmitted(
                    report.OmittedHiddenCharacterFindingCount
                )
            );
        }
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

    private static void WriteJsonFailure(CliOutputFormat format, TextWriter output)
    {
        if (format == CliOutputFormat.Json)
        {
            CliJsonWriter.WriteAuditFailure(output);
        }
    }

    private void RenderLinks(IReadOnlyList<LinkAuditReport> reports)
    {
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
