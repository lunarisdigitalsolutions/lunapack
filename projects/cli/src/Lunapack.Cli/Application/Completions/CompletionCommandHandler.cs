using System.CommandLine;

namespace Lunapack.Cli.Application.Completions;

internal sealed class CompletionCommandHandler(
    RootCommand rootCommand,
    CliConsole console,
    CompletionScriptInstallerResolver scriptInstallerResolver
)
{
    public Command CreateCompleteCommand()
    {
        var commandLineArgument = new Argument<string>("command-line")
        {
            Description = "Complete Luna command line.",
        };
        var positionOption = new Option<int?>("--position")
        {
            Description = "Cursor position within command line.",
        };
        var command = new Command("complete", "Return command-line completion candidates.")
        {
            commandLineArgument,
            positionOption,
        };
        command.Hidden = true;
        command.SetAction(parseResult =>
        {
            var commandLine = parseResult.GetValue(commandLineArgument);
            if (commandLine is null)
            {
                return console.Fail("A command line is required.");
            }

            var position = parseResult.GetValue(positionOption) ?? commandLine.Length;
            if (position < 0)
            {
                return console.Fail("Completion position must be within the command line.");
            }

            var completionParseResult = rootCommand.Parse(commandLine);
            if (position > commandLine.Length)
            {
                if (completionParseResult.CommandResult.Command.Arguments.Count == 0)
                {
                    return 0;
                }

                commandLine = commandLine.PadRight(position);
                completionParseResult = rootCommand.Parse(commandLine);
            }

            foreach (var completion in completionParseResult.GetCompletions(position))
            {
                parseResult.InvocationConfiguration.Output.WriteLine(completion.Label);
            }

            return 0;
        });
        return command;
    }

    public Command CreateCompletionsCommand()
    {
        var shellArgument = new Argument<string?>("shell")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Shell name: bash, fish, nushell, pwsh, or zsh.",
        };
        shellArgument.CompletionSources.Add("bash", "fish", "nushell", "pwsh", "zsh");
        var installOption = new Option<bool>("--install")
        {
            Description = "Append the completion script to the shell's user configuration.",
        };
        var scriptCommand = new Command("script", "Generate a shell completion script.")
        {
            shellArgument,
            installOption,
        };
        scriptCommand.SetAction(parseResult =>
        {
            var shell = parseResult.GetValue(shellArgument);
            var script = ShellCompletionScriptGenerator.Generate(shell);
            if (script is null)
            {
                return console.Fail(
                    "Shell could not be inferred. Specify bash, fish, nushell, pwsh, or zsh."
                );
            }

            if (parseResult.GetValue(installOption))
            {
                var selectedShell = ShellCompletionScriptGenerator.ResolveShell(shell);
                if (selectedShell is null)
                {
                    return console.Fail("Shell could not be inferred.");
                }

                var scriptInstaller = scriptInstallerResolver.Resolve(selectedShell);
                var plan = scriptInstaller.CreatePlan(script);
                console.Info($"Completion script:\n{plan.Script}");
                console.Info($"Destination: {plan.DestinationPath}");
                if (!console.Confirm("Append this script?", defaultValue: false))
                {
                    console.Info("Completion installation canceled.");
                    return 0;
                }

                scriptInstaller.Install(plan);
                console.Success($"Installed completion script in {plan.DestinationPath}");
                return 0;
            }

            parseResult.InvocationConfiguration.Output.Write(script);
            return 0;
        });
        return new Command("completions", "Generate shell completion configuration.")
        {
            scriptCommand,
        };
    }
}
