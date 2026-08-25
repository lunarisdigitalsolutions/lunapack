using System.CommandLine;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class DiscoverPacksCommandHandler(
    CatalogService catalogService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
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
        var command = new Command("discover", "List available packs.") { versionCountOption };
        command.SetAction(parseResult =>
            DiscoverAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(versionCountOption) ?? DefaultVersionCount
            )
        );

        return command;
    }

    public async Task<int> DiscoverAsync(string projectDirectory, int versionCount)
    {
        if (versionCount is < 1 or > PackCatalog.MaximumVersionCount)
        {
            return console.Fail(
                $"The version limit must be between 1 and {PackCatalog.MaximumVersionCount}."
            );
        }

        var catalog = await console.RunWithStatusAsync(
            "Discovering available packs...",
            () => catalogService.LoadAsync(projectDirectory)
        );
        if (catalog.Value is not { } catalogPacks)
        {
            return console.Fail(catalog.Error);
        }

        var packs = PackCatalog
            .GetRecentReleases(catalogPacks, versionCount)
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
        return 0;
    }
}
