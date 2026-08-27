using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunapack.Cli;

internal static class PackAuthoringFormatter
{
    public static IReadOnlyList<IRenderable> FormatList(PackManifest manifest)
    {
        var files = CreateTable("Managed content", "Type", "Selector", "Target");
        foreach (var managedFile in manifest.ManagedFiles)
        {
            var (type, selector) = GetSelector(managedFile);
            files.AddRow(
                Markup.Escape(type),
                Markup.Escape(selector),
                Markup.Escape(managedFile.Target)
            );
        }

        var references = CreateTable("Referenced packs", "ID", "Version");
        foreach (var reference in manifest.Packs)
        {
            references.AddRow(Markup.Escape(reference.Id), Markup.Escape(reference.Version));
        }

        var hooks = CreateTable("Lifecycle hooks", "Event");
        foreach (var (eventName, _, _) in GetHooks(manifest.Hooks))
        {
            hooks.AddRow(Markup.Escape(eventName));
        }

        return [files, references, hooks];
    }

    public static IReadOnlyList<IRenderable> FormatHooks(PackManifest manifest)
    {
        var table = CreateTable("Lifecycle hooks", "Event", "Position", "Type", "Details");
        var hooks = GetHooks(manifest.Hooks);
        if (hooks.Count == 0)
        {
            table.AddRow("No lifecycle hooks declared.", "-", "-", "-");
        }

        foreach (var (eventName, position, hook) in hooks)
        {
            var isInstruction = string.Equals(hook.Type, "instruction", StringComparison.Ordinal);
            table.AddRow(
                Markup.Escape(eventName),
                position.ToString(CultureInfo.InvariantCulture),
                Markup.Escape(hook.Type),
                Markup.Escape(
                    isInstruction
                        ? $"{hook.File ?? "-"}; templating: {(hook.Templating == true ? "enabled" : "disabled")}"
                        : $"{FormatInvocation(hook)}; description: {hook.Description ?? "-"}"
                )
            );
        }

        return [table];
    }

    public static IReadOnlyList<IRenderable> FormatSummary(PackManifest manifest)
    {
        var table = CreateTable("Pack", "Field", "Value");
        table.AddRow("ID", Markup.Escape(manifest.Id));
        table.AddRow("Name", Markup.Escape(manifest.Name ?? "-"));
        table.AddRow("Version", Markup.Escape(manifest.Version));
        table.AddRow("Description", Markup.Escape(manifest.Description ?? "-"));
        table.AddRow("Author", Markup.Escape(manifest.Author ?? "-"));
        table.AddRow("Homepage", Markup.Escape(manifest.Homepage ?? "-"));
        table.AddRow("License", Markup.Escape(manifest.License ?? "-"));
        table.AddRow(
            "Managed files",
            manifest.ManagedFiles.Count.ToString(CultureInfo.InvariantCulture)
        );
        table.AddRow(
            "Scripts",
            GetHooks(manifest.Hooks).Count.ToString(CultureInfo.InvariantCulture)
        );
        table.AddRow("References", manifest.Packs.Count.ToString(CultureInfo.InvariantCulture));
        table.AddRow(
            "Parameters",
            manifest.Parameters.Count.ToString(CultureInfo.InvariantCulture)
        );
        table.AddRow("Tags", manifest.Tags.Count.ToString(CultureInfo.InvariantCulture));
        return [table];
    }

    private static Table CreateTable(string title, params string[] columns)
    {
        var table = new Table()
            .Title($"[bold]{Markup.Escape(title)}[/]")
            .Border(TableBorder.Rounded);
        foreach (var column in columns)
        {
            table.AddColumn($"[bold]{Markup.Escape(column)}[/]");
        }

        return table;
    }

    private static (string Type, string Selector) GetSelector(
        PackManifest.PackManagedFile managedFile
    )
    {
        if (managedFile.Source is { } source)
        {
            return ("file", source);
        }

        if (managedFile.Directory is { } directory)
        {
            return ("directory", directory);
        }

        return ("glob", managedFile.Glob ?? "-");
    }

    private static List<(string Event, int Position, PackManifest.PackHook Hook)> GetHooks(
        PackManifest.PackHooks? hooks
    )
    {
        if (hooks is null)
        {
            return [];
        }

        var values = new List<(string, int, PackManifest.PackHook)>();
        AddHooks(values, "preInstall", hooks.PreInstall);
        AddHooks(values, "postInstall", hooks.PostInstall);
        AddHooks(values, "preUpdate", hooks.PreUpdate);
        AddHooks(values, "postUpdate", hooks.PostUpdate);
        return values;
    }

    private static void AddHooks(
        List<(string Event, int Position, PackManifest.PackHook Hook)> hooks,
        string eventName,
        List<PackManifest.PackHook>? declarations
    )
    {
        if (declarations is null)
        {
            return;
        }

        for (var index = 0; index < declarations.Count; index++)
        {
            hooks.Add((eventName, index + 1, declarations[index]));
        }
    }

    private static string FormatInvocation(PackManifest.PackHook script)
    {
        var executable = script.Runner ?? script.Command ?? "-";
        var arguments = script.File is null
            ? script.Arguments
            : new[] { script.File }.Concat(script.Arguments);
        return string.Join(" ", new[] { executable }.Concat(arguments.Select(EscapeArgument)));
    }

    private static string EscapeArgument(string argument) =>
        argument.Any(char.IsWhiteSpace) || argument.Contains('"')
            ? $"\"{argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
}
