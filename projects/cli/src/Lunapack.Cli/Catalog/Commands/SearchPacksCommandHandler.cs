using System.CommandLine;
using System.Diagnostics;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Links;
using Lunapack.Cli.Project;
using Spectre.Console;

namespace Lunapack.Cli.Catalog.Commands;

internal sealed class SearchPacksCommandHandler(
    CatalogService catalogService,
    LinkInspectionService linkInspectionService,
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
        var searchTermArgument = new Argument<string>("term") { Description = "Pack search term." };
        var versionCountOption = new Option<int?>("--versions", "-v")
        {
            Description = "Maximum versions to display for each package.",
        };
        versionCountOption.CompletionSources.Add([
            .. Enumerable
                .Range(1, 10)
                .Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ]);
        var allowDraftOption = new Option<bool>("--allow-draft")
        {
            Description = "Include draft packs.",
        };
        var command = new Command("search", "Search available packs.")
        {
            searchTermArgument,
            versionCountOption,
            allowDraftOption,
        };
        command.SetAction(async parseResult =>
        {
            var searchTerm = parseResult.GetValue(searchTermArgument);
            var versionCount = parseResult.GetValue(versionCountOption) ?? DefaultVersionCount;
            return searchTerm is null
                ? console.Fail("Search term must not be empty.")
                : await SearchAsync(
                    workspaceDirectoryResolver.Resolve(
                        projectDirectory,
                        parseResult.GetValue(workspaceOption)
                    ),
                    searchTerm,
                    versionCount,
                    parseResult.GetValue(allowDraftOption)
                );
        });

        return command;
    }

    public async Task<int> SearchAsync(
        string projectDirectory,
        string searchTerm,
        int versionCount,
        bool allowDraft = false
    )
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return console.Fail("Search term must not be empty.");
        }

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
            "Searching available packs...",
            () => catalogService.LoadAsync(projectDirectory)
        );
        if (catalog.Value is not { } catalogPacks)
        {
            return console.Fail(catalog.Error);
        }

        var normalizedSearchTerm = searchTerm.Trim();
        var searchablePacks = allowDraft
            ? catalogPacks
            : [.. catalogPacks.Where(pack => !pack.Manifest.Draft)];
        var packs = PackCatalog.Search(searchablePacks, normalizedSearchTerm);
        var linkSummaries = await linkInspectionService.ListAsync(projectDirectory);
        if (linkSummaries.Value is not { } links)
        {
            return console.Fail(linkSummaries.Error);
        }

        var matchingLinks = SearchLinks(links, normalizedSearchTerm);
        if (packs.Count == 0 && matchingLinks.Count == 0)
        {
            return console.Fail($"No packs or links were found for '{normalizedSearchTerm}'.");
        }

        var releases = PackCatalog.GetRecentReleases(packs, versionCount);
        if (releases.Count > 0)
        {
            console.Render(CreatePackTable(releases));
        }

        if (matchingLinks.Count > 0)
        {
            console.Render(LinkOutputFormatter.CreateListTable(matchingLinks));
        }

        console.Info(
            $"Found {releases.Count} matching packs and {matchingLinks.Count} matching links ({CliDuration.Format(Stopwatch.GetElapsedTime(startedAt))})."
        );
        nextStepRenderer.Render(nextStepAdvisor.Recommend(NextStepContext.PacksSearched));
        return 0;
    }

    private static Table CreatePackTable(IReadOnlyList<CatalogPack> packs)
    {
        var table = new Table().Title("[bold]Search results[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Pack[/]");
        table.AddColumn("[bold]Version[/]");
        table.AddColumn("[bold]Description[/]");
        foreach (var pack in packs)
        {
            table.AddRow(
                Markup.Escape(pack.Manifest.Id),
                Markup.Escape(pack.Manifest.Version),
                Markup.Escape(pack.Manifest.Description ?? "-")
            );
        }

        return table;
    }

    private static IReadOnlyList<LinkSummary> SearchLinks(
        IReadOnlyList<LinkSummary> links,
        string searchTerm
    ) =>
        [
            .. links.Where(link =>
                link.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                || link.Source.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                || link.Target.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
            ),
        ];
}
