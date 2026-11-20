using System.CommandLine;
using Lunapack.Cli.Application;

namespace Lunapack.Cli.Application.Preferences;

internal sealed class ConfigCommandHandler(
    CliPreferenceService preferenceService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
)
{
    private static readonly string[] _preferenceKeys =
    [
        "discover.only-composites",
        "git.allow-insecure-transport",
        "search.only-composites",
        "next-steps.enabled",
        .. ResourceLimits.Definitions.Select(definition => definition.Name),
    ];

    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("config", "Manage LunaPack preferences and resource limits.");
        command.Subcommands.Add(CreateSetCommand(projectDirectory, workspaceOption));
        command.Subcommands.Add(CreateListCommand(projectDirectory, workspaceOption));
        command.Subcommands.Add(CreateRemoveCommand(projectDirectory, workspaceOption));
        return command;
    }

    private Command CreateSetCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var keyArgument = CreatePreferenceKeyArgument();
        var valueArgument = new Argument<string>("value")
        {
            Description = "Boolean preference or positive resource-limit value.",
        };
        var command = new Command("set", "Set a preference or resource limit.")
        {
            keyArgument,
            valueArgument,
        };
        var scopeOptions = AddScopeOptions(command);
        command.SetAction(async parseResult =>
        {
            var key = parseResult.GetValue(keyArgument);
            var value = parseResult.GetValue(valueArgument);
            if (key is null || value is null)
            {
                return console.Fail("A configuration key and value are required.");
            }

            if (!TryGetScope(parseResult, scopeOptions, out var scope))
            {
                return console.Fail("Choose only one preference scope.");
            }

            var projectPath = workspaceDirectoryResolver.Resolve(
                projectDirectory,
                parseResult.GetValue(workspaceOption)
            );
            var result = await preferenceService.SetAsync(projectPath, key, value, scope);
            return result.Value ? 0 : console.Fail(result.Error);
        });
        return command;
    }

    private Command CreateListCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("list", "List settings in selected scope.");
        var scopeOptions = AddScopeOptions(command);
        command.SetAction(async parseResult =>
        {
            if (!TryGetScope(parseResult, scopeOptions, out var scope))
            {
                return console.Fail("Choose only one preference scope.");
            }

            var projectPath = workspaceDirectoryResolver.Resolve(
                projectDirectory,
                parseResult.GetValue(workspaceOption)
            );
            var result = await preferenceService.ListAsync(projectPath, scope);
            if (result.Value is not { } values)
            {
                return console.Fail(result.Error);
            }

            if (values.Count == 0)
            {
                console.Info("No settings are configured in the selected scope.");
                return 0;
            }

            foreach (var (key, value) in values)
            {
                console.Info($"{key}={value}");
            }

            return 0;
        });
        return command;
    }

    private Command CreateRemoveCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var keyArgument = CreatePreferenceKeyArgument();
        var command = new Command("remove", "Remove a preference or resource limit.")
        {
            keyArgument,
        };
        var scopeOptions = AddScopeOptions(command);
        command.SetAction(async parseResult =>
        {
            var key = parseResult.GetValue(keyArgument);
            if (key is null)
            {
                return console.Fail("A configuration key is required.");
            }

            if (!TryGetScope(parseResult, scopeOptions, out var scope))
            {
                return console.Fail("Choose only one preference scope.");
            }

            var projectPath = workspaceDirectoryResolver.Resolve(
                projectDirectory,
                parseResult.GetValue(workspaceOption)
            );
            var result = await preferenceService.RemoveAsync(projectPath, key, scope);
            return result.Value ? 0 : console.Fail(result.Error);
        });
        return command;
    }

    private static Argument<string> CreatePreferenceKeyArgument()
    {
        var argument = new Argument<string>("key")
        {
            Description = "Preference or resource-limit key.",
        };
        argument.CompletionSources.Add(_preferenceKeys);
        return argument;
    }

    private static ScopeOptions AddScopeOptions(Command command)
    {
        var projectOption = new Option<bool>("--project", "--local")
        {
            Description = "Store preference or limit in the current lunapack.yml.",
        };
        var userProjectOption = new Option<bool>("--user-project")
        {
            Description = "Store preference or limit for the current project in user settings.",
        };
        var globalOption = new Option<bool>("--global")
        {
            Description = "Store preference or limit in global user settings (default).",
        };
        command.Options.Add(projectOption);
        command.Options.Add(userProjectOption);
        command.Options.Add(globalOption);
        return new ScopeOptions(projectOption, userProjectOption, globalOption);
    }

    private static bool TryGetScope(
        ParseResult parseResult,
        ScopeOptions options,
        out CliPreferenceService.Scope scope
    )
    {
        var project = parseResult.GetValue(options.Project);
        var userProject = parseResult.GetValue(options.UserProject);
        var global = parseResult.GetValue(options.Global);
        if ((project ? 1 : 0) + (userProject ? 1 : 0) + (global ? 1 : 0) > 1)
        {
            scope = default;
            return false;
        }

        scope =
            project ? CliPreferenceService.Scope.Local
            : userProject ? CliPreferenceService.Scope.UserProject
            : CliPreferenceService.Scope.Global;
        return true;
    }

    private sealed record ScopeOptions(
        Option<bool> Project,
        Option<bool> UserProject,
        Option<bool> Global
    );
}
