using System.CommandLine;

namespace Lunapack.Cli.Application;

internal sealed class CompletionCommandHandler(RootCommand rootCommand, CliConsole console)
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
            if (position < 0 || position > commandLine.Length)
            {
                return console.Fail("Completion position must be within the command line.");
            }

            foreach (var completion in rootCommand.Parse(commandLine).GetCompletions(position))
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
        var scriptCommand = new Command("script", "Generate a shell completion script.")
        {
            shellArgument,
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

            parseResult.InvocationConfiguration.Output.Write(script);
            return 0;
        });
        return new Command("completions", "Generate shell completion configuration.")
        {
            scriptCommand,
        };
    }
}
