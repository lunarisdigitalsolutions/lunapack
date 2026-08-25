using System.CommandLine;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class SearchPacksCommandHandler(
    CatalogService catalogService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
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
        var command = new Command("search", "Search available packs.")
        {
            searchTermArgument,
            versionCountOption,
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
                    versionCount
                );
        });

        return command;
    }

    public async Task<int> SearchAsync(string projectDirectory, string searchTerm, int versionCount)
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

        var catalog = await console.RunWithStatusAsync(
            "Searching available packs...",
            () => catalogService.LoadAsync(projectDirectory)
        );
        if (catalog.Value is not { } catalogPacks)
        {
            return console.Fail(catalog.Error);
        }

        var normalizedSearchTerm = searchTerm.Trim();
        var packs = PackCatalog.Search(catalogPacks, normalizedSearchTerm);
        if (packs.Count == 0)
        {
            return console.Fail($"No packs were found for '{normalizedSearchTerm}'.");
        }

        var table = new Table().Title("[bold]Search results[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Pack[/]");
        table.AddColumn("[bold]Version[/]");
        table.AddColumn("[bold]Description[/]");
        foreach (var pack in PackCatalog.GetRecentReleases(packs, versionCount))
        {
            table.AddRow(
                Markup.Escape(pack.Manifest.Id),
                Markup.Escape(pack.Manifest.Version),
                Markup.Escape(pack.Manifest.Description ?? "-")
            );
        }

        console.Render(table);
        return 0;
    }
}
