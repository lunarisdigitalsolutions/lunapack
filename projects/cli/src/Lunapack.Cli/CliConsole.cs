using Lunapack.Cli.Packs;
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

    public void Success(string message) => WriteStyledInfo("green", message);

    public void Accent(string message) => WriteStyledInfo("cyan", message);

    public void Warning(string message) => Write(CliLogLevel.Warning, message);

    public void Error(string message) => Write(CliLogLevel.Error, message);

    public void Render(IRenderable renderable) => ansiConsole.Write(renderable);

    public bool Confirm(string prompt, bool defaultValue = true)
    {
        var defaultChoice = defaultValue ? "Y" : "N";
        ansiConsole.Markup(
            $"{Markup.Escape(prompt)} [[{(defaultValue ? "Y/n" : "y/N")}]] ({defaultChoice}) "
        );
        var response = ansiConsole.Input.ReadKey(intercept: false);
        return response is null || response.Value.Key == ConsoleKey.Enter
            ? defaultValue
            : response.Value.KeyChar is 'y' or 'Y';
    }

    public bool WaitForContinue()
    {
        Info("Press Enter to continue...");
        try
        {
            ConsoleKeyInfo? key;
            do
            {
                key = ansiConsole.Input.ReadKey(intercept: true);
            } while (key is not null && key.Value.Key != ConsoleKey.Enter);

            return key is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public string Prompt(PackParameterPrompt parameter)
    {
        var prompt = FormatParameterPrompt(parameter);
        return parameter.Definition.Type switch
        {
            PackParameterType.String => ansiConsole.Prompt(
                CreateTextPrompt(prompt, parameter.Definition.Default as string)
            ),
            PackParameterType.Bool => Confirm(prompt, parameter.Definition.Default as bool? ?? true)
                .ToString()
                .ToLowerInvariant(),
            PackParameterType.Enum => ansiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(prompt)
                    .AddChoices(OrderChoices(parameter.Definition))
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported parameter type '{parameter.Definition.Type}'."
            ),
        };
    }

    public IReadOnlyList<string> PromptValues(PackParameterPrompt parameter)
    {
        if (!parameter.Definition.Multiple)
        {
            return [Prompt(parameter)];
        }

        var prompt = new MultiSelectionPrompt<string>()
            .Title(FormatParameterPrompt(parameter))
            .NotRequired()
            .AddChoices(parameter.Definition.Values);
        if (parameter.Definition.Default is IEnumerable<object> defaultValues)
        {
            foreach (var defaultValue in defaultValues.OfType<string>())
            {
                prompt.Select(defaultValue);
            }
        }

        return ansiConsole.Prompt(prompt);
    }

    public string PromptText(string prompt, string? defaultValue = null)
    {
        var textPrompt = new TextPrompt<string>(Markup.Escape(prompt));
        if (defaultValue is not null)
        {
            textPrompt.DefaultValue(defaultValue);
        }

        return ansiConsole.Prompt(textPrompt);
    }

    public Task<T> RunWithStatusAsync<T>(string description, Func<Task<T>> action) =>
        IsEnabled(CliLogLevel.Info)
            ? ansiConsole
                .Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(description, _ => action())
            : action();

    private bool IsEnabled(CliLogLevel level) => level >= minimumLevel;

    private static TextPrompt<string> CreateTextPrompt(string prompt, string? defaultValue)
    {
        var textPrompt = new TextPrompt<string>(prompt);
        return defaultValue is null ? textPrompt : textPrompt.DefaultValue(defaultValue);
    }

    private static IEnumerable<string> OrderChoices(PackParameterDefinition definition) =>
        definition.Default is not string defaultValue
            ? definition.Values
            :
            [
                defaultValue,
                .. definition.Values.Where(value =>
                    !string.Equals(value, defaultValue, StringComparison.Ordinal)
                ),
            ];

    private void WriteStyledInfo(string style, string message)
    {
        if (IsEnabled(CliLogLevel.Info))
        {
            ansiConsole.MarkupLine($"[{style}]{Markup.Escape(message)}[/]");
        }
    }

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
