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

        var scripts = CreateTable("Lifecycle scripts", "Hook");
        foreach (var (hook, _) in GetScripts(manifest.Scripts))
        {
            scripts.AddRow(Markup.Escape(hook));
        }

        return [files, references, scripts];
    }

    public static IReadOnlyList<IRenderable> FormatScripts(PackManifest manifest)
    {
        var table = CreateTable("Lifecycle scripts", "Hook", "Invocation", "Description");
        foreach (var (hook, script) in GetScripts(manifest.Scripts))
        {
            table.AddRow(
                Markup.Escape(hook),
                Markup.Escape(FormatInvocation(script)),
                Markup.Escape(script.Description ?? "-")
            );
        }

        return [table];
    }

    public static IReadOnlyList<IRenderable> FormatSources(PackManifest manifest)
    {
        var table = CreateTable(
            "External sources",
            "Alias",
            "Repository",
            "Ref",
            "Path",
            "References"
        );
        foreach (
            var (alias, source) in manifest.Sources.OrderBy(
                item => item.Key,
                StringComparer.Ordinal
            )
        )
        {
            var fingerprint = SourceIdentityNormalizer.CreateGit(
                source.Url,
                source.Ref,
                source.Path
            );
            var referenceCount = manifest.ManagedFiles.Count(file =>
                string.Equals(
                    PackManagedFileSelector.Create(file).Value?.SourceAlias,
                    alias,
                    StringComparison.Ordinal
                )
            );
            table.AddRow(
                Markup.Escape(alias),
                Markup.Escape(fingerprint.Value?.Identity ?? "invalid"),
                Markup.Escape(source.Ref),
                Markup.Escape(fingerprint.Value?.Path ?? "/"),
                referenceCount.ToString(CultureInfo.InvariantCulture)
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
            "External sources",
            manifest.Sources.Count.ToString(CultureInfo.InvariantCulture)
        );
        table.AddRow(
            "Scripts",
            GetScripts(manifest.Scripts).Count.ToString(CultureInfo.InvariantCulture)
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
        var selector = PackManagedFileSelector.Create(managedFile).Value;
        if (selector is not null)
        {
            return (selector.Kind.ToString().ToLowerInvariant(), selector.Value);
        }

        return ("invalid", "-");
    }

    private static List<(string Hook, PackManifest.LifecycleScript Script)> GetScripts(
        PackManifest.PackScripts? scripts
    )
    {
        if (scripts is null)
        {
            return [];
        }

        var values = new List<(string, PackManifest.LifecycleScript)>();
        AddScript(values, "preInstall", scripts.PreInstall);
        AddScript(values, "postInstall", scripts.PostInstall);
        AddScript(values, "preUpdate", scripts.PreUpdate);
        AddScript(values, "postUpdate", scripts.PostUpdate);
        return values;
    }

    private static void AddScript(
        List<(string Hook, PackManifest.LifecycleScript Script)> scripts,
        string hook,
        PackManifest.LifecycleScript? script
    )
    {
        if (script is not null)
        {
            scripts.Add((hook, script));
        }
    }

    private static string FormatInvocation(PackManifest.LifecycleScript script)
    {
        var executable = script.Runner ?? script.Command ?? "-";
        var arguments = script.File is null
            ? script.Arguments
            : new[] { script.File }.Concat(script.Arguments);
        return string.Join(" ", new[] { executable }.Concat(arguments));
    }
}
