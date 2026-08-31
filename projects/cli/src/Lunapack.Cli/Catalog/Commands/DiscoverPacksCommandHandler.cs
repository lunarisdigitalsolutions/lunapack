using System.CommandLine;
using System.Diagnostics;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Project;
using Spectre.Console;

namespace Lunapack.Cli.Catalog.Commands;

internal sealed class DiscoverPacksCommandHandler(
    CatalogService catalogService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    NextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    WorkflowPrerequisiteGuard prerequisiteGuard,
    CliConsole console
)
{
    private const int DefaultVersionCount = 1;

    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var versionCountOption = new Option<int?>("--versions", "-v")
        {
            Description = "Maximum versions to display for each package.",
        };
        versionCountOption.CompletionSources.Add(
            Enumerable
                .Range(1, 10)
                .Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ToArray()
        );
        var allowDraftOption = new Option<bool>("--allow-draft")
        {
            Description = "Include draft packs.",
        };
        var command = new Command("discover", "List available packs.")
        {
            versionCountOption,
            allowDraftOption,
        };
        command.SetAction(parseResult =>
            DiscoverAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(versionCountOption) ?? DefaultVersionCount,
                parseResult.GetValue(allowDraftOption)
            )
        );

        return command;
    }

    public async Task<int> DiscoverAsync(
        string projectDirectory,
        int versionCount,
        bool allowDraft = false
    )
    {
        if (versionCount is < 1 or > PackCatalog.MaximumVersionCount)
        {
            return console.Fail(
                $"The version limit must be between 1 and {PackCatalog.MaximumVersionCount}."
            );
        }

        var prerequisiteFailure = await prerequisiteGuard.RequireSourcesAsync(projectDirectory);
        if (prerequisiteFailure is not null)
        {
            return prerequisiteFailure.Value;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var catalog = await console.RunWithStatusAsync(
            "Discovering available packs...",
            () => catalogService.LoadAsync(projectDirectory)
        );
        if (catalog.Value is not { } catalogPacks)
        {
            return console.Fail(catalog.Error);
        }

        var packs = PackCatalog
            .GetRecentReleases(
                allowDraft ? catalogPacks : [.. catalogPacks.Where(pack => !pack.Manifest.Draft)],
                versionCount
            )
            .OrderBy(pack => pack.Manifest.Id, StringComparer.Ordinal)
            .ThenByDescending(pack => pack.Version, NuGet.Versioning.VersionComparer.VersionRelease)
            .ToList();
        if (packs.Count == 0)
        {
            return console.Fail("No packs were found in configured sources.");
        }

        var table = new Table().Title("[bold]Available packs[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Pack[/]");
        table.AddColumn("[bold]Version[/]");
        table.AddColumn("[bold]Description[/]");
        table.AddColumn("[bold]Tags[/]");
        foreach (var pack in packs)
        {
            table.AddRow(
                Markup.Escape(pack.Manifest.Id),
                Markup.Escape(pack.Manifest.Version),
                Markup.Escape(pack.Manifest.Description ?? "-"),
                Markup.Escape(
                    pack.Manifest.Tags.Count == 0 ? "-" : string.Join(", ", pack.Manifest.Tags)
                )
            );
        }

        console.Render(table);
        console.Info(
            $"Found {packs.Count} packs ({CliDuration.Format(Stopwatch.GetElapsedTime(startedAt))})."
        );
        nextStepRenderer.Render(nextStepAdvisor.Recommend(NextStepContext.PacksDiscovered));
        return 0;
    }
}
