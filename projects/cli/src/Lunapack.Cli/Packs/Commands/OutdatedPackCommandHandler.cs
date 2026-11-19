using System.CommandLine;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Diagnostics;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Spectre.Console;

namespace Lunapack.Cli.Packs.Commands;

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
        var formatOption = CliOutputFormatOption.Create();
        command.Options.Add(formatOption);
        command.SetAction(async parseResult =>
        {
            if (!CliOutputFormatOption.TryParse(parseResult.GetValue(formatOption), out var format))
            {
                return console.Fail(
                    CliOutputFormatOption.FormatUnsupportedMessage(
                        parseResult.GetValue(formatOption)
                    )
                );
            }

            return await OutdatedAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(offlineOption),
                format,
                parseResult.InvocationConfiguration.Output
            );
        });

        return command;
    }

    public async Task<int> OutdatedAsync(
        string projectDirectory,
        bool offline,
        CliOutputFormat format,
        TextWriter output
    )
    {
        var prerequisiteFailure = await prerequisiteGuard.RequireSourcesAsync(projectDirectory);
        if (prerequisiteFailure is not null)
        {
            WriteJsonFailure(format, output);
            return prerequisiteFailure.Value;
        }

        var availableUpdates = await updateSelectionService.GetAvailableAsync(
            projectDirectory,
            offline
        );
        if (availableUpdates.Value is not { } availability)
        {
            return Fail(availableUpdates.Error, format, output);
        }

        var updates = availability.Updates;
        if (offline)
        {
            console.Info(
                "Remote refs were not checked; results use available lock and cache evidence."
            );
        }

        IReadOnlyList<LinkOutdatedReport> outdatedLinks = [];
        if (!offline)
        {
            var reportedLinks = await linkLifecycleService.OutdatedAsync(projectDirectory);
            if (reportedLinks.Value is not { } resolvedLinks)
            {
                return Fail(reportedLinks.Error, format, output);
            }

            outdatedLinks = resolvedLinks;
        }

        if (format == CliOutputFormat.Json)
        {
            CliJsonWriter.WriteOutdated(
                output,
                OutdatedJsonMapper.Map(availability, outdatedLinks, remoteRefsChecked: !offline)
            );
            return availability.IntegrityViolations.Count > 0 ? 1 : 0;
        }

        RenderAvailableUpdates(updates);
        RenderOciStatus(availability);

        if (outdatedLinks.Count > 0)
        {
            console.Render(CreateOutdatedLinkTable(outdatedLinks));
        }

        if (
            updates.Count == 0
            && availability.IntegrityViolations.Count == 0
            && availability.UncheckedOciPacks.Count == 0
            && outdatedLinks.Count == 0
        )
        {
            console.Info("No updates are available.");
        }

        return availability.IntegrityViolations.Count > 0 ? 1 : 0;
    }

    private int Fail(string? message, CliOutputFormat format, TextWriter output)
    {
        var exitCode = console.Fail(message);
        WriteJsonFailure(format, output);
        return exitCode;
    }

    private static void WriteJsonFailure(CliOutputFormat format, TextWriter output)
    {
        if (format == CliOutputFormat.Json)
        {
            CliJsonWriter.WriteOutdated(output, null);
        }
    }

    private void RenderAvailableUpdates(IReadOnlyList<AvailablePackUpdate> updates)
    {
        if (updates.Count == 0)
        {
            return;
        }

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

    private void RenderOciStatus(PackUpdateAvailability availability)
    {
        if (availability.IntegrityViolations.Count > 0)
        {
            console.Warning(
                "OCI version tags no longer match their locked release digests; publish a new version instead of replacing an existing release."
            );
            console.Render(CreateOciIntegrityTable(availability.IntegrityViolations));
        }

        if (availability.UncheckedOciPacks.Count > 0)
        {
            console.Render(CreateUncheckedOciTable(availability.UncheckedOciPacks));
        }
    }

    private static Table CreateOciIntegrityTable(
        IReadOnlyList<OciReleaseIntegrityViolation> violations
    )
    {
        var table = new Table().Title("[bold]OCI release integrity violations[/]");
        table.AddColumn("[bold]Pack[/]");
        table.AddColumn("[bold]Locked manifest[/]");
        table.AddColumn("[bold]Observed manifest[/]");
        foreach (var violation in violations)
        {
            table.AddRow(
                Markup.Escape(
                    $"{violation.LockedPack.Id}@{violation.LockedPack.Version} ({violation.LockedPack.SourceName})"
                ),
                Markup.Escape(violation.LockedPack.OciSource!.ManifestDigest),
                Markup.Escape(violation.ObservedPack?.OciSource?.ManifestDigest ?? "missing")
            );
        }

        return table;
    }

    private static Table CreateUncheckedOciTable(IReadOnlyList<ProjectLockFile.ResolvedPack> packs)
    {
        var table = new Table().Title("[bold]OCI releases not checked (offline)[/]");
        table.AddColumn("[bold]Pack[/]");
        table.AddColumn("[bold]Version[/]");
        table.AddColumn("[bold]Source[/]");
        foreach (var pack in packs)
        {
            table.AddRow(
                Markup.Escape(pack.Id),
                Markup.Escape(pack.Version),
                Markup.Escape(pack.SourceName ?? "unknown")
            );
        }

        return table;
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
