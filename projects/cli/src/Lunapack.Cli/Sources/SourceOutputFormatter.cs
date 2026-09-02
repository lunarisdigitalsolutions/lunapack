using System.Globalization;
using Lunapack.Cli.Project;
using Spectre.Console;

namespace Lunapack.Cli.Sources;

internal static class SourceOutputFormatter
{
    public static Table CreateListTable(IReadOnlyList<ProjectConfiguration.Source> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var table = new Table().Title("[bold]Sources[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Name[/]");
        table.AddColumn("[bold]Type[/]");
        table.AddColumn("[bold]Details[/]");

        foreach (var source in sources)
        {
            table.AddRow(
                Markup.Escape(source.Name),
                source is ProjectConfiguration.LocalSource ? "local" : "git",
                FormatDetails(source)
            );
        }

        return table;
    }

    public static string Format(ProjectConfiguration.Source source) =>
        source switch
        {
            ProjectConfiguration.LocalSource localSource =>
                $"{localSource.Name} - local - path: {localSource.Path} - identity: {FormatIdentity(ConfiguredSourceIdentity.Create(localSource))}",
            ProjectConfiguration.GitSource gitSource => FormatGit(gitSource),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    private static string FormatDetails(ProjectConfiguration.Source source) =>
        source switch
        {
            ProjectConfiguration.LocalSource localSource =>
                $"[bold]Path:[/] {Markup.Escape(localSource.Path)}\n[bold]Identity:[/] {Markup.Escape(FormatIdentity(ConfiguredSourceIdentity.Create(localSource)))}",
            ProjectConfiguration.GitSource gitSource => FormatGitDetails(gitSource),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    private static string FormatGitDetails(ProjectConfiguration.GitSource source)
    {
        var details = new List<string> { $"[bold]URL:[/] {Markup.Escape(source.Url)}" };

        if (source.Ref is not null)
        {
            details.Add($"[bold]Ref:[/] {Markup.Escape(source.Ref)}");
        }

        if (source.Path is not null)
        {
            details.Add($"[bold]Path:[/] {Markup.Escape(source.Path)}");
        }

        if (source.TimeoutSeconds is { } timeoutSeconds)
        {
            details.Add(
                $"[bold]Timeout:[/] {timeoutSeconds.ToString(CultureInfo.InvariantCulture)}s"
            );
        }

        details.Add(
            $"[bold]Identity:[/] {Markup.Escape(FormatIdentity(ConfiguredSourceIdentity.Create(source)))}"
        );
        return string.Join('\n', details);
    }

    private static string FormatGit(ProjectConfiguration.GitSource source)
    {
        var properties = new List<string> { $"url: {source.Url}" };

        if (source.Ref is not null)
        {
            properties.Add($"ref: {source.Ref}");
        }

        if (source.Path is not null)
        {
            properties.Add($"path: {source.Path}");
        }

        if (source.TimeoutSeconds is { } timeoutSeconds)
        {
            properties.Add($"timeoutSeconds: {timeoutSeconds}");
        }

        properties.Add($"identity: {FormatIdentity(ConfiguredSourceIdentity.Create(source))}");
        return $"{source.Name} - git - {string.Join(" - ", properties)}";
    }

    internal static string FormatIdentity(ConfiguredSourceIdentity identity) =>
        identity.Type switch
        {
            "local" => $"local(path={identity.Path})",
            "git" =>
                $"git(url={identity.Url}, ref={identity.Ref ?? "<default>"}, path={identity.Path ?? "<root>"})",
            _ => throw new ArgumentOutOfRangeException(nameof(identity)),
        };
}
