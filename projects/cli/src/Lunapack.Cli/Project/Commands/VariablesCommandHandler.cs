using System.CommandLine;
using Lunapack.Cli.Application;
using Spectre.Console;

namespace Lunapack.Cli.Project.Commands;

internal sealed class VariablesCommandHandler(
    ProjectStateStore projectStateStore,
    CliCompletionProvider completionProvider,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("variables", "Manage project variables.");
        command.Subcommands.Add(CreateListCommand(projectDirectory, workspaceOption));
        command.Subcommands.Add(CreateSetCommand(projectDirectory, workspaceOption));
        command.Subcommands.Add(CreateRemoveCommand(projectDirectory, workspaceOption));
        return command;
    }

    private Command CreateListCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("list", "List project variables.");
        command.SetAction(parseResult =>
            ListAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                )
            )
        );
        return command;
    }

    private Command CreateSetCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var nameArgument = new Argument<string>("name") { Description = "Variable name." };
        var valueArgument = new Argument<string>("value") { Description = "Variable value." };
        var command = new Command("set", "Set a project variable.") { nameArgument, valueArgument };
        command.SetAction(async parseResult =>
        {
            var name = parseResult.GetValue(nameArgument);
            var value = parseResult.GetValue(valueArgument);
            if (name is null || value is null)
            {
                return console.Fail("A variable name and value are required.");
            }

            return await SetAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                name,
                value
            );
        });
        return command;
    }

    private Command CreateRemoveCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var nameArgument = new Argument<string>("name") { Description = "Variable name." };
        nameArgument.CompletionSources.Add(completionProvider.GetConfiguredVariableNames);
        var command = new Command("rm", "Remove a project variable.") { nameArgument };
        command.SetAction(async parseResult =>
        {
            var name = parseResult.GetValue(nameArgument);
            if (name is null)
            {
                return console.Fail("A variable name is required.");
            }

            return await RemoveAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                name
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

        if (projectState.Configuration.Variables.Count == 0)
        {
            console.Info("No project variables are configured.");
            return 0;
        }

        var table = CreateTable();
        var orderedVariables = projectState.Configuration.Variables.OrderBy(
            variable => variable.Key,
            StringComparer.Ordinal
        );
        foreach (var variable in orderedVariables)
        {
            table.AddRow(Markup.Escape(variable.Key), Markup.Escape(FormatValue(variable.Value)));
        }

        console.Render(table);
        return 0;
    }

    private async Task<int> SetAsync(string projectDirectory, string name, string value)
    {
        if (!IsVariableName(name))
        {
            return console.Fail($"Invalid variable name '{name}'.");
        }

        var state = await projectStateStore.LoadAsync(projectDirectory);
        if (state.Value is not { } projectState)
        {
            return console.Fail(state.Error);
        }

        var variables = new Dictionary<string, object>(
            projectState.Configuration.Variables,
            StringComparer.Ordinal
        )
        {
            [name] = value,
        };
        var saved = await projectStateStore.SaveAsync(
            projectDirectory,
            projectState with
            {
                Configuration = projectState.Configuration with { Variables = variables },
            }
        );
        return saved.Value ? 0 : console.Fail(saved.Error);
    }

    private async Task<int> RemoveAsync(string projectDirectory, string name)
    {
        if (!IsVariableName(name))
        {
            return console.Fail($"Invalid variable name '{name}'.");
        }

        var state = await projectStateStore.LoadAsync(projectDirectory);
        if (state.Value is not { } projectState)
        {
            return console.Fail(state.Error);
        }

        var variables = new Dictionary<string, object>(
            projectState.Configuration.Variables,
            StringComparer.Ordinal
        );
        if (!variables.Remove(name))
        {
            return console.Fail($"No project variable is configured for '{name}'.");
        }

        var saved = await projectStateStore.SaveAsync(
            projectDirectory,
            projectState with
            {
                Configuration = projectState.Configuration with { Variables = variables },
            }
        );
        return saved.Value ? 0 : console.Fail(saved.Error);
    }

    private static Table CreateTable()
    {
        var table = new Table().Title("[bold]Project variables[/]").Border(TableBorder.Rounded);
        table.AddColumn("[bold]Name[/]");
        table.AddColumn("[bold]Value[/]");
        return table;
    }

    private static string FormatValue(object value) =>
        value switch
        {
            string stringValue => stringValue,
            bool booleanValue => booleanValue.ToString().ToLowerInvariant(),
            _ => value.ToString() ?? "-",
        };

    private static bool IsVariableName(string name)
    {
        if (name.Length == 0 || !IsVariableNameStart(name[0]))
        {
            return false;
        }

        return name.Skip(1)
            .All(character =>
                IsVariableNameStart(character) || (character >= '0' && character <= '9')
            );
    }

    private static bool IsVariableNameStart(char character) =>
        (character >= 'A' && character <= 'Z')
        || (character >= 'a' && character <= 'z')
        || character == '_';
}
