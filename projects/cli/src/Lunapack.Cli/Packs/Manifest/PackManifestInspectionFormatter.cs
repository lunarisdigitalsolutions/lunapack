using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Project;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunapack.Cli.Packs.Manifest;

internal static class PackManifestInspectionFormatter
{
    public static IReadOnlyList<IRenderable> Format(
        PackManifest manifest,
        ProjectConfiguration.Remapping? remapping = null,
        ProjectConfiguration.Remapping? fallbackRemapping = null
    )
    {
        var renderables = new List<IRenderable> { CreateDetailsTable(manifest) };
        if (manifest.ManagedFiles.Count > 0)
        {
            renderables.Add(
                CreateManagedFilesTable(manifest.ManagedFiles, remapping, fallbackRemapping)
            );
        }

        if (manifest.Parameters.Count > 0)
        {
            renderables.Add(CreateParameterTable(manifest.Parameters));
        }

        renderables.Add(CreateLifecycleHooksTable(manifest.Hooks));

        if (manifest.Packs.Count > 0)
        {
            renderables.Add(CreateReferencedPacksTable(manifest.Packs));
        }

        return renderables;
    }

    private static Table CreateManagedFilesTable(
        IReadOnlyList<PackManifest.PackManagedFile> managedFiles,
        ProjectConfiguration.Remapping? remapping,
        ProjectConfiguration.Remapping? fallbackRemapping
    )
    {
        var targetRemapping = ManagedFileTargetRemapping.FromConfiguration(remapping);
        var fallbackTargetRemapping = ManagedFileTargetRemapping.FromConfiguration(
            fallbackRemapping
        );
        var table = CreateTable("Managed files");
        table.AddColumn("[bold]Target[/]");
        foreach (
            var managedFile in managedFiles.OrderBy(file => file.Target, StringComparer.Ordinal)
        )
        {
            var declaredTarget = managedFile.Target;
            var effectiveTarget = targetRemapping.Resolve(declaredTarget, fallbackTargetRemapping);
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
        table.AddRow("Draft", manifest.Draft ? "yes" : "no");
        table.AddRow("Description", Markup.Escape(manifest.Description ?? "-"));
        table.AddRow("License", Markup.Escape(manifest.License ?? "-"));
        table.AddRow("Author", Markup.Escape(manifest.Author ?? "-"));
        table.AddRow("Homepage", Markup.Escape(manifest.Homepage ?? "-"));
        table.AddRow("Lifecycle hooks", HasHooks(manifest.Hooks) ? "declared" : "none");
        table.AddRow(
            "Tags",
            Markup.Escape(manifest.Tags.Count == 0 ? "-" : string.Join(", ", manifest.Tags.Take(5)))
        );
        return table;
    }

    private static Table CreateLifecycleHooksTable(PackManifest.PackHooks? hooks)
    {
        var table = CreateTable("Lifecycle hooks");
        table.AddColumn("[bold]Event[/]");
        table.AddColumn("[bold]Position[/]");
        table.AddColumn("[bold]Type[/]");
        table.AddColumn("[bold]Details[/]");
        var events = new (string Name, IReadOnlyList<PackManifest.PackHook>? Hooks)[]
        {
            ("preInstall", hooks?.PreInstall),
            ("postInstall", hooks?.PostInstall),
            ("preUpdate", hooks?.PreUpdate),
            ("postUpdate", hooks?.PostUpdate),
            ("preUninstall", hooks?.PreUninstall),
            ("postUninstall", hooks?.PostUninstall),
        };
        var hasHooks = HasHooks(hooks);
        foreach (var (name, declarations) in events)
        {
            if (hasHooks && declarations is not { Count: > 0 })
            {
                table.AddRow(name, "-", "-", "none");
                continue;
            }

            for (var index = 0; index < (declarations?.Count ?? 0); index++)
            {
                var hook = declarations![index];
                var isInstruction = string.Equals(
                    hook.Type,
                    "instruction",
                    StringComparison.Ordinal
                );
                table.AddRow(
                    name,
                    (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Markup.Escape(hook.Type),
                    Markup.Escape(
                        isInstruction
                            ? $"{hook.File ?? "-"}; templating: {(hook.Templating == true ? "enabled" : "disabled")}"
                            : $"{FormatInvocation(hook)}; description: {hook.Description ?? "none"}"
                    )
                );
            }
        }

        if (!hasHooks)
        {
            table.AddRow("No lifecycle hooks declared.", "-", "-", "-");
        }

        return table;
    }

    private static bool HasHooks(PackManifest.PackHooks? hooks) =>
        hooks is not null
        && new[]
        {
            hooks.PreInstall,
            hooks.PostInstall,
            hooks.PreUpdate,
            hooks.PostUpdate,
            hooks.PreUninstall,
            hooks.PostUninstall,
        }.Any(static declarations => declarations?.Count > 0);

    private static string FormatInvocation(PackManifest.PackHook script)
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
        table.AddColumn("[bold]Multiple[/]");
        table.AddColumn("[bold]Values[/]");
        table.AddColumn("[bold]Default[/]");
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
                declaration.Multiple == true ? "yes" : "no",
                Markup.Escape(
                    declaration.Values is { Count: > 0 }
                        ? string.Join(", ", declaration.Values)
                        : "-"
                ),
                Markup.Escape(FormatParameterDefault(declaration.Default)),
                declaration.Required ? "yes" : "no"
            );
        }

        return table;
    }

    private static string FormatParameterDefault(object? value) =>
        value switch
        {
            null => "-",
            IEnumerable<object> values => $"[{string.Join(", ", values)}]",
            _ => value.ToString() ?? "-",
        };

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
