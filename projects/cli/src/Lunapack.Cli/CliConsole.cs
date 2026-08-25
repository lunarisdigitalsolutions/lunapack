using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunapack.Cli;

internal sealed class CliConsole(IAnsiConsole ansiConsole, CliLogLevel minimumLevel)
{
    public bool IsInteractive => ansiConsole.Profile.Capabilities.Interactive;

    public int Fail(string? message)
    {
        Error(message ?? "Command failed.");
        return 1;
    }

    public void Verbose(string message) => Write(CliLogLevel.Verbose, message);

    public void Debug(string message) => Write(CliLogLevel.Debug, message);

    public void Info(string message) => Write(CliLogLevel.Info, message);

    public void Warning(string message) => Write(CliLogLevel.Warning, message);

    public void Error(string message) => Write(CliLogLevel.Error, message);

    public void Render(IRenderable renderable) => ansiConsole.Write(renderable);

    public bool Confirm(string prompt) => ansiConsole.Confirm(prompt);

    public string Prompt(PackParameterPrompt parameter)
    {
        var prompt = FormatParameterPrompt(parameter);
        return parameter.Definition.Type switch
        {
            PackParameterType.String => ansiConsole.Prompt(new TextPrompt<string>(prompt)),
            PackParameterType.Bool => ansiConsole.Confirm(prompt).ToString().ToLowerInvariant(),
            PackParameterType.Enum => ansiConsole.Prompt(
                new SelectionPrompt<string>().Title(prompt).AddChoices(parameter.Definition.Values)
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported parameter type '{parameter.Definition.Type}'."
            ),
        };
    }

    public Task<T> RunWithStatusAsync<T>(string description, Func<Task<T>> action) =>
        IsEnabled(CliLogLevel.Info)
            ? ansiConsole
                .Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(description, _ => action())
            : action();

    private bool IsEnabled(CliLogLevel level) => level >= minimumLevel;

    private static string FormatParameterPrompt(PackParameterPrompt parameter)
    {
        var displayName = parameter.Definition.DisplayName ?? parameter.Id;
        var description = parameter.Definition.Description;
        var details = description is null ? string.Empty : $"[grey]{Markup.Escape(description)}[/]";
        return $"[bold]{Markup.Escape(displayName)}[/] ({details})\n";
    }

    private void Write(CliLogLevel level, string message)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        if (level == CliLogLevel.Info)
        {
            ansiConsole.WriteLine(message);
            return;
        }

        var style = level switch
        {
            CliLogLevel.Verbose => "grey",
            CliLogLevel.Debug => "blue",
            CliLogLevel.Warning => "yellow",
            CliLogLevel.Error => "red",
            _ => throw new InvalidOperationException($"Unsupported log level '{level}'."),
        };
        ansiConsole.MarkupLine(
            $"[{style}]{level.ToString().ToLowerInvariant()}:[/] {Markup.Escape(message)}"
        );
    }
}
