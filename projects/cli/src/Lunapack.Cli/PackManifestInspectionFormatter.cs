using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunapack.Cli;

internal static class PackManifestInspectionFormatter
{
    public static IReadOnlyList<IRenderable> Format(
        PackManifest manifest,
        ProjectConfiguration.Remapping? remapping = null
    )
    {
        var renderables = new List<IRenderable> { CreateDetailsTable(manifest) };
        if (manifest.ManagedFiles.Count > 0)
        {
            renderables.Add(CreateManagedFilesTable(manifest.ManagedFiles, remapping));
        }

        if (manifest.Parameters.Count > 0)
        {
            renderables.Add(CreateParameterTable(manifest.Parameters));
        }

        renderables.Add(CreateLifecycleScriptsTable(manifest.Scripts));

        if (manifest.Packs.Count > 0)
        {
            renderables.Add(CreateReferencedPacksTable(manifest.Packs));
        }

        return renderables;
    }

    private static Table CreateManagedFilesTable(
        IReadOnlyList<PackManifest.PackManagedFile> managedFiles,
        ProjectConfiguration.Remapping? remapping
    )
    {
        var targetRemapping = ManagedFileTargetRemapping.FromConfiguration(remapping);
        var table = CreateTable("Managed files");
        table.AddColumn("[bold]Target[/]");
        foreach (
            var managedFile in managedFiles.OrderBy(file => file.Target, StringComparer.Ordinal)
        )
        {
            var declaredTarget = managedFile.Target;
            var effectiveTarget = targetRemapping.Resolve(declaredTarget);
            var target = string.Equals(declaredTarget, effectiveTarget, StringComparison.Ordinal)
                ? declaredTarget
                : $"{declaredTarget} -> {effectiveTarget}";
            table.AddRow(Markup.Escape(target));
        }

        return table;
    }

    private static Table CreateDetailsTable(PackManifest manifest)
    {
        var table = CreateTable("Pack");
        table.AddColumn("[bold]Field[/]");
        table.AddColumn("[bold]Value[/]");
        table.AddRow("ID", Markup.Escape(manifest.Id));
        table.AddRow("Name", Markup.Escape(manifest.Name ?? "-"));
        table.AddRow("Version", Markup.Escape(manifest.Version));
        table.AddRow("Description", Markup.Escape(manifest.Description ?? "-"));
        table.AddRow("License", Markup.Escape(manifest.License ?? "-"));
        table.AddRow("Author", Markup.Escape(manifest.Author ?? "-"));
        table.AddRow("Homepage", Markup.Escape(manifest.Homepage ?? "-"));
        table.AddRow("Lifecycle scripts", manifest.Scripts is null ? "none" : "declared");
        table.AddRow(
            "Tags",
            Markup.Escape(manifest.Tags.Count == 0 ? "-" : string.Join(", ", manifest.Tags.Take(5)))
        );
        return table;
    }

    private static Table CreateLifecycleScriptsTable(PackManifest.PackScripts? scripts)
    {
        var table = CreateTable("Lifecycle scripts");
        table.AddColumn("[bold]Hook[/]");
        table.AddColumn("[bold]Description[/]");
        table.AddColumn("[bold]Invocation[/]");
        var hooks = new (string Name, PackManifest.LifecycleScript? Script)[]
        {
            ("preInstall", scripts?.PreInstall),
            ("postInstall", scripts?.PostInstall),
            ("preUpdate", scripts?.PreUpdate),
            ("postUpdate", scripts?.PostUpdate),
        };
        foreach (var (name, script) in hooks)
        {
            table.AddRow(
                name,
                Markup.Escape(script?.Description ?? "none"),
                Markup.Escape(script is null ? "none" : FormatInvocation(script))
            );
        }

        return table;
    }

    private static string FormatInvocation(PackManifest.LifecycleScript script)
    {
        var executable = script.Runner ?? script.Command ?? "-";
        var arguments = script.File is { } file
            ? new[] { file }.Concat(script.Arguments)
            : script.Arguments;
        return string.Join(" ", new[] { executable }.Concat(arguments.Select(EscapeArgument)));
    }

    private static string EscapeArgument(string argument) =>
        argument.Any(char.IsWhiteSpace) || argument.Contains('"')
            ? $"\"{argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;

    private static Table CreateParameterTable(
        IReadOnlyDictionary<string, PackManifest.PackParameter> parameters
    )
    {
        var table = CreateTable("Parameters");
        table.AddColumn("[bold]ID[/]");
        table.AddColumn("[bold]Display name[/]");
        table.AddColumn("[bold]Description[/]");
        table.AddColumn("[bold]Type[/]");
        table.AddColumn("[bold]Required[/]");
        foreach (
            var parameter in parameters.OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
        )
        {
            var declaration = parameter.Value;
            table.AddRow(
                Markup.Escape(parameter.Key),
                Markup.Escape(declaration.DisplayName ?? parameter.Key),
                Markup.Escape(declaration.Description ?? "-"),
                Markup.Escape(declaration.Type),
                declaration.Required ? "yes" : "no"
            );
        }

        return table;
    }

    private static Table CreateReferencedPacksTable(
        IReadOnlyList<PackManifest.PackReference> references
    )
    {
        var table = CreateTable("Referenced packs");
        table.AddColumn("[bold]ID[/]");
        table.AddColumn("[bold]Version[/]");
        table.AddColumn("[bold]Disabled hooks[/]");
        foreach (var reference in references)
        {
            table.AddRow(
                Markup.Escape(reference.Id),
                Markup.Escape(reference.Version),
                Markup.Escape(
                    reference.DisabledHooks.Count == 0
                        ? "none"
                        : string.Join(", ", reference.DisabledHooks)
                )
            );
        }

        return table;
    }

    private static Table CreateTable(string title) =>
        new Table().Title($"[bold]{Markup.Escape(title)}[/]").Border(TableBorder.Rounded);
}
