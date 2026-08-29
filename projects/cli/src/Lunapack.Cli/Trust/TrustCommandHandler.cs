using System.CommandLine;
using Lunapack.Cli.Application;

namespace Lunapack.Cli.Trust;

internal sealed class TrustCommandHandler(
    TrustService trustService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var sourceCommand = CreateSourceCommand(projectDirectory, workspaceOption);
        var packCommand = CreatePackCommand(projectDirectory, workspaceOption);
        var scriptsCommand = CreateScriptsCommand(projectDirectory, workspaceOption);
        var listCommand = CreateListCommand(projectDirectory, workspaceOption);
        var revokeCommand = CreateRevokeCommand(projectDirectory, workspaceOption);
        return new Command("trust", "Manage lifecycle script trust.")
        {
            sourceCommand,
            packCommand,
            scriptsCommand,
            listCommand,
            revokeCommand,
        };
    }

    private Command CreateScriptsCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    ) =>
        new("scripts", "Manage blanket lifecycle script denial.")
        {
            CreateDenyScriptsCommand(projectDirectory, workspaceOption),
            CreateResetScriptsCommand(projectDirectory, workspaceOption),
        };

    private Command CreateDenyScriptsCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var projectOption = CreateProjectOption();
        var globalOption = CreateGlobalOption();
        var command = new Command("deny", "Deny all lifecycle scripts.")
        {
            projectOption,
            globalOption,
        };
        command.SetAction(async parseResult =>
        {
            if (
                !TryGetScope(
                    parseResult.GetValue(projectOption),
                    parseResult.GetValue(globalOption),
                    out var scope
                )
            )
            {
                return console.Fail("The --project and --global options are mutually exclusive.");
            }

            var result = await trustService.DenyScriptsAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                scope
            );
            return result.IsSuccess ? 0 : console.Fail(result.Error);
        });
        return command;
    }

    private Command CreateResetScriptsCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var projectOption = CreateProjectOption();
        var globalOption = CreateGlobalOption();
        var command = new Command("reset", "Reset lifecycle script denial.")
        {
            projectOption,
            globalOption,
        };
        command.SetAction(async parseResult =>
        {
            if (
                !TryGetScope(
                    parseResult.GetValue(projectOption),
                    parseResult.GetValue(globalOption),
                    out var scope
                )
            )
            {
                return console.Fail("The --project and --global options are mutually exclusive.");
            }

            var result = await trustService.ResetScriptDenialAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                scope
            );
            return result.IsSuccess ? 0 : console.Fail(result.Error);
        });
        return command;
    }

    private Command CreateListCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var projectOption = CreateProjectOption();
        var globalOption = CreateGlobalOption();
        var command = new Command("list", "List persisted lifecycle script trust.")
        {
            projectOption,
            globalOption,
        };
        command.SetAction(async parseResult =>
        {
            if (
                !TryGetScope(
                    parseResult.GetValue(projectOption),
                    parseResult.GetValue(globalOption),
                    out var scope
                )
            )
            {
                return console.Fail("The --project and --global options are mutually exclusive.");
            }

            var result = await trustService.ListAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                scope
            );
            if (result.Value is not { } listing)
            {
                return console.Fail(result.Error);
            }

            foreach (var line in TrustOutputFormatter.Format(listing))
            {
                console.Info(line);
            }

            return 0;
        });
        return command;
    }

    private Command CreateRevokeCommand(string projectDirectory, Option<string?> workspaceOption) =>
        new("revoke", "Revoke persisted lifecycle script trust.")
        {
            CreateRevokeSourceCommand(projectDirectory, workspaceOption),
            CreateRevokePackCommand(projectDirectory, workspaceOption),
        };

    private Command CreateRevokeSourceCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var namesArgument = new Argument<string[]>("name")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "Configured source names whose trust will be revoked.",
        };
        var projectOption = CreateProjectOption();
        var globalOption = CreateGlobalOption();
        var command = new Command("source", "Revoke source lifecycle script trust.")
        {
            namesArgument,
            projectOption,
            globalOption,
        };
        command.SetAction(async parseResult =>
        {
            if (
                !TryGetScope(
                    parseResult.GetValue(projectOption),
                    parseResult.GetValue(globalOption),
                    out var scope
                )
            )
            {
                return console.Fail("The --project and --global options are mutually exclusive.");
            }

            var result = await trustService.RevokeSourcesAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(namesArgument) ?? [],
                scope
            );
            return result.IsSuccess ? 0 : console.Fail(result.Error);
        });
        return command;
    }

    private Command CreateRevokePackCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var idsArgument = new Argument<string[]>("id")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "Bare pack IDs whose trust will be revoked.",
        };
        var sourceOption = new Option<string?>("--source", "-s")
        {
            Description = "Configured source name for the pack IDs.",
        };
        var projectOption = CreateProjectOption();
        var globalOption = CreateGlobalOption();
        var command = new Command("pack", "Revoke pack lifecycle script trust.")
        {
            idsArgument,
            sourceOption,
            projectOption,
            globalOption,
        };
        command.SetAction(async parseResult =>
        {
            var sourceName = parseResult.GetValue(sourceOption);
            if (string.IsNullOrEmpty(sourceName))
            {
                return console.Fail("The --source option is required for pack trust revocation.");
            }

            if (
                !TryGetScope(
                    parseResult.GetValue(projectOption),
                    parseResult.GetValue(globalOption),
                    out var scope
                )
            )
            {
                return console.Fail("The --project and --global options are mutually exclusive.");
            }

            var result = await trustService.RevokePacksAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(idsArgument) ?? [],
                sourceName,
                scope
            );
            return result.IsSuccess ? 0 : console.Fail(result.Error);
        });
        return command;
    }

    private Command CreateSourceCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var namesArgument = new Argument<string[]>("name")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "Configured source names to trust.",
        };
        var projectOption = CreateProjectOption();
        var globalOption = CreateGlobalOption();
        var command = new Command("source", "Trust lifecycle scripts from configured sources.")
        {
            namesArgument,
            projectOption,
            globalOption,
        };
        command.SetAction(async parseResult =>
        {
            if (
                !TryGetScope(
                    parseResult.GetValue(projectOption),
                    parseResult.GetValue(globalOption),
                    out var scope
                )
            )
            {
                return console.Fail("The --project and --global options are mutually exclusive.");
            }

            var names = parseResult.GetValue(namesArgument) ?? [];
            var result = await trustService.TrustSourcesAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                names,
                scope
            );
            return result.IsSuccess ? 0 : console.Fail(result.Error);
        });
        return command;
    }

    private Command CreatePackCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var idsArgument = new Argument<string[]>("id")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "Bare pack IDs to trust.",
        };
        var sourceOption = new Option<string?>("--source", "-s")
        {
            Description = "Configured source name for the pack IDs.",
        };
        var projectOption = CreateProjectOption();
        var globalOption = CreateGlobalOption();
        var command = new Command("pack", "Trust lifecycle scripts from specific packs.")
        {
            idsArgument,
            sourceOption,
            projectOption,
            globalOption,
        };
        command.SetAction(async parseResult =>
        {
            var sourceName = parseResult.GetValue(sourceOption);
            if (string.IsNullOrEmpty(sourceName))
            {
                return console.Fail("The --source option is required for pack trust.");
            }

            if (
                !TryGetScope(
                    parseResult.GetValue(projectOption),
                    parseResult.GetValue(globalOption),
                    out var scope
                )
            )
            {
                return console.Fail("The --project and --global options are mutually exclusive.");
            }

            var ids = parseResult.GetValue(idsArgument) ?? [];
            var result = await trustService.TrustPacksAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                ids,
                sourceName,
                scope
            );
            return result.IsSuccess ? 0 : console.Fail(result.Error);
        });
        return command;
    }

    private static Option<bool> CreateProjectOption() =>
        new("--project") { Description = "Declare trust in this project configuration." };

    private static Option<bool> CreateGlobalOption() =>
        new("--global") { Description = "Trust for the current user across all projects." };

    private static bool TryGetScope(bool project, bool global, out TrustScope scope)
    {
        scope =
            project ? TrustScope.Project
            : global ? TrustScope.GlobalUser
            : TrustScope.LocalUser;
        return !project || !global;
    }
}
