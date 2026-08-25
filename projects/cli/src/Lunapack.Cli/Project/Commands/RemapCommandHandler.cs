using System.CommandLine;
using System.IO.Abstractions;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class RemapCommandHandler(
    IFileSystem fileSystem,
    ProjectStateStore projectStateStore,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("remap", "Manage managed-file target remappings.");
        command.Subcommands.Add(CreateListCommand(projectDirectory, workspaceOption));
        command.Subcommands.Add(CreateSetCommand(projectDirectory, workspaceOption));
        command.Subcommands.Add(CreateRemoveCommand(projectDirectory, workspaceOption));
        return command;
    }

    private Command CreateListCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("list", "List managed-file target remappings.");
        command.SetAction(parseResult =>
            ListAsync(ResolveWorkspaceDirectory(projectDirectory, workspaceOption, parseResult))
        );
        return command;
    }

    private Command CreateSetCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var kindArgument = CreateKindArgument();
        var targetArgument = CreateTargetArgument();
        var newTargetArgument = new Argument<string>("new-target")
        {
            Description = "Effective project-relative target.",
        };
        var command = new Command("set", "Create or replace a managed-file target remapping.")
        {
            kindArgument,
            targetArgument,
            newTargetArgument,
        };
        command.SetAction(async parseResult =>
        {
            var kind = parseResult.GetValue(kindArgument);
            var target = parseResult.GetValue(targetArgument);
            var newTarget = parseResult.GetValue(newTargetArgument);
            if (kind is null || target is null || newTarget is null)
            {
                return console.Fail("A remapping kind, target, and new target are required.");
            }

            return await SetAsync(
                ResolveWorkspaceDirectory(projectDirectory, workspaceOption, parseResult),
                kind,
                target,
                newTarget
            );
        });
        return command;
    }

    private Command CreateRemoveCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var kindArgument = CreateKindArgument();
        var targetArgument = CreateTargetArgument();
        var command = new Command("rm", "Remove a managed-file target remapping.")
        {
            kindArgument,
            targetArgument,
        };
        command.SetAction(async parseResult =>
        {
            var kind = parseResult.GetValue(kindArgument);
            var target = parseResult.GetValue(targetArgument);
            if (kind is null || target is null)
            {
                return console.Fail("A remapping kind and target are required.");
            }

            return await RemoveAsync(
                ResolveWorkspaceDirectory(projectDirectory, workspaceOption, parseResult),
                kind,
                target
            );
        });
        return command;
    }

    private async Task<int> ListAsync(string projectDirectory)
    {
        var state = await projectStateStore.LoadAsync(projectDirectory);
        if (state.Value is not { } projectState)
        {
            return console.Fail(state.Error);
        }

        var remapping = projectState.Configuration.Remap;
        if (remapping is null || (remapping.Directories.Count == 0 && remapping.Files.Count == 0))
        {
            console.Info("No managed-file target remappings are configured.");
            return 0;
        }

        var table = new Table()
            .Title("[bold]Managed target remappings[/]")
            .Border(TableBorder.Rounded);
        table.AddColumn("[bold]Kind[/]");
        table.AddColumn("[bold]Target[/]");
        table.AddColumn("[bold]New target[/]");
        AddMappings(table, "directory", remapping.Directories);
        AddMappings(table, "file", remapping.Files);
        console.Render(table);
        return 0;
    }

    private async Task<int> SetAsync(
        string projectDirectory,
        string kind,
        string target,
        string newTarget
    )
    {
        var paths = ParsePaths(projectDirectory, kind, target, newTarget);
        if (paths.Value is not { } normalizedPaths)
        {
            return console.Fail(paths.Error);
        }

        var state = await projectStateStore.LoadAsync(projectDirectory);
        if (state.Value is not { } projectState)
        {
            return console.Fail(state.Error);
        }

        var remapping = projectState.Configuration.Remap ?? new ProjectConfiguration.Remapping();
        var updatedRemapping = string.Equals(
            normalizedPaths.Kind,
            "directory",
            StringComparison.Ordinal
        )
            ? remapping with
            {
                Directories = SetMapping(
                    remapping.Directories,
                    normalizedPaths.Target,
                    normalizedPaths.NewTarget!
                ),
            }
            : remapping with
            {
                Files = SetMapping(
                    remapping.Files,
                    normalizedPaths.Target,
                    normalizedPaths.NewTarget!
                ),
            };
        return await SaveAsync(projectDirectory, projectState, updatedRemapping);
    }

    private async Task<int> RemoveAsync(string projectDirectory, string kind, string target)
    {
        var paths = ParsePaths(projectDirectory, kind, target);
        if (paths.Value is not { } normalizedPaths)
        {
            return console.Fail(paths.Error);
        }

        var state = await projectStateStore.LoadAsync(projectDirectory);
        if (state.Value is not { } projectState)
        {
            return console.Fail(state.Error);
        }

        var remapping = projectState.Configuration.Remap;
        if (remapping is null)
        {
            return console.Fail(
                $"No {normalizedPaths.Kind} remapping is configured for '{normalizedPaths.Target}'."
            );
        }

        var mappings = string.Equals(normalizedPaths.Kind, "directory", StringComparison.Ordinal)
            ? remapping.Directories
            : remapping.Files;
        if (!mappings.ContainsKey(normalizedPaths.Target))
        {
            return console.Fail(
                $"No {normalizedPaths.Kind} remapping is configured for '{normalizedPaths.Target}'."
            );
        }

        var updatedRemapping = string.Equals(
            normalizedPaths.Kind,
            "directory",
            StringComparison.Ordinal
        )
            ? remapping with
            {
                Directories = RemoveMapping(remapping.Directories, normalizedPaths.Target),
            }
            : remapping with
            {
                Files = RemoveMapping(remapping.Files, normalizedPaths.Target),
            };
        return await SaveAsync(projectDirectory, projectState, updatedRemapping);
    }

    private ManifestOperationResult<RemapPaths> ParsePaths(
        string projectDirectory,
        string kind,
        string target,
        string? newTarget = null
    )
    {
        if (!IsSupportedKind(kind))
        {
            return ManifestOperationResult<RemapPaths>.Failure(
                "Remapping kind must be 'directory' or 'file'."
            );
        }

        var normalizedTarget = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            target
        );
        if (normalizedTarget.Value is not { } parsedTarget)
        {
            return ManifestOperationResult<RemapPaths>.Failure(
                $"Invalid remap target '{target}': {normalizedTarget.Error}"
            );
        }

        if (newTarget is null)
        {
            return ManifestOperationResult<RemapPaths>.Success(
                new RemapPaths(kind, parsedTarget, null)
            );
        }

        var normalizedNewTarget = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            newTarget
        );
        return normalizedNewTarget.Value is { } parsedNewTarget
            ? ManifestOperationResult<RemapPaths>.Success(
                new RemapPaths(kind, parsedTarget, parsedNewTarget)
            )
            : ManifestOperationResult<RemapPaths>.Failure(
                $"Invalid remap new target '{newTarget}': {normalizedNewTarget.Error}"
            );
    }

    private async Task<int> SaveAsync(
        string projectDirectory,
        ProjectState projectState,
        ProjectConfiguration.Remapping remapping
    )
    {
        var saved = await projectStateStore.SaveAsync(
            projectDirectory,
            projectState with
            {
                Configuration = projectState.Configuration with
                {
                    Remap =
                        remapping.Directories.Count == 0 && remapping.Files.Count == 0
                            ? null
                            : remapping,
                },
            }
        );
        return saved.Value ? 0 : console.Fail(saved.Error);
    }

    private static Argument<string> CreateKindArgument() =>
        new("kind") { Description = "Remapping kind: directory or file." };

    private static Argument<string> CreateTargetArgument() =>
        new("target") { Description = "Declared project-relative target to remap." };

    private static void AddMappings(
        Table table,
        string kind,
        IReadOnlyDictionary<string, string> mappings
    )
    {
        foreach (
            var (target, newTarget) in mappings.OrderBy(
                mapping => mapping.Key,
                StringComparer.Ordinal
            )
        )
        {
            table.AddRow(Markup.Escape(kind), Markup.Escape(target), Markup.Escape(newTarget));
        }
    }

    private static Dictionary<string, string> SetMapping(
        IReadOnlyDictionary<string, string> mappings,
        string target,
        string newTarget
    ) => new Dictionary<string, string>(mappings, StringComparer.Ordinal) { [target] = newTarget };

    private static Dictionary<string, string> RemoveMapping(
        IReadOnlyDictionary<string, string> mappings,
        string target
    )
    {
        var updatedMappings = new Dictionary<string, string>(mappings, StringComparer.Ordinal);
        _ = updatedMappings.Remove(target);
        return updatedMappings;
    }

    private string ResolveWorkspaceDirectory(
        string projectDirectory,
        Option<string?> workspaceOption,
        ParseResult parseResult
    ) =>
        workspaceDirectoryResolver.Resolve(projectDirectory, parseResult.GetValue(workspaceOption));

    private static bool IsSupportedKind(string kind) =>
        string.Equals(kind, "directory", StringComparison.Ordinal)
        || string.Equals(kind, "file", StringComparison.Ordinal);

    private sealed record RemapPaths(string Kind, string Target, string? NewTarget);
}
