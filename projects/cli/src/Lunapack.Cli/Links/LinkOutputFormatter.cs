using System.Globalization;
using Spectre.Console;

namespace Lunapack.Cli;

internal static class LinkOutputFormatter
{
    public static Table CreateListTable(IReadOnlyList<LinkSummary> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        var table = new Table().Title("[bold]Links[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Link[/]");
        table.AddColumn("[bold]Source[/]");
        table.AddColumn("[bold]Target[/]");
        table.AddColumn("[bold]Status[/]");
        foreach (var link in links)
        {
            table.AddRow(
                Markup.Escape(link.Name),
                Markup.Escape(link.Source),
                Markup.Escape(link.Target),
                Markup.Escape(link.Status)
            );
        }

        return table;
    }

    public static Table CreateDetailTable(LinkDetail link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var table = new Table()
            .Title($"[bold]Link {Markup.Escape(link.Summary.Name)}[/]")
            .Border(TableBorder.Rounded);
        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");
        table.AddRow("Source", Markup.Escape(link.Summary.Source));
        table.AddRow("Effective ref", Markup.Escape(link.EffectiveRef ?? "-"));
        table.AddRow("Resolved commit", Markup.Escape(link.ResolvedCommit ?? "-"));
        table.AddRow("Base path", Markup.Escape(link.BasePath));
        table.AddRow("Includes", Markup.Escape(Join(link.Includes)));
        table.AddRow("Excludes", Markup.Escape(Join(link.Excludes)));
        table.AddRow("Strip prefix", Markup.Escape(link.StripPrefix ?? "-"));
        table.AddRow("Flatten", link.Flatten ? "yes" : "no");
        table.AddRow("Target", Markup.Escape(link.Summary.Target));
        table.AddRow("Status", Markup.Escape(link.Summary.Status));
        table.AddRow(
            "Selected files",
            link.Summary.IsInstalled
                ? link.Summary.SelectedFileCount.ToString(CultureInfo.InvariantCulture)
                : "-"
        );
        table.AddRow(
            "Locally modified files",
            link.Summary.IsInstalled
                ? link.Summary.ModifiedFileCount.ToString(CultureInfo.InvariantCulture)
                : "-"
        );
        return table;
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "-" : string.Join(", ", values);
}
