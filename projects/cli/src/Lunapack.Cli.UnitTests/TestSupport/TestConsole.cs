using Spectre.Console;

namespace Lunapack.Cli.UnitTests;

internal static class TestConsole
{
    public static CliConsole Create() => new(CreateAnsiConsole(), CliLogLevel.Info);

    public static IAnsiConsole CreateAnsiConsole() =>
        AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(TextWriter.Null),
            }
        );
}
