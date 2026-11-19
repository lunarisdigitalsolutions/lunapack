using Spectre.Console;

namespace Lunapack.Cli.Application.Diagnostics;

internal sealed record CliInvocationStreams(
    TextWriter CommandOutput,
    TextWriter RawError,
    TextWriter DiagnosticOutput,
    IAnsiConsole DiagnosticConsole,
    bool RoutesDiagnosticsToError,
    bool IsSbomCommand,
    string? JsonCommand
)
{
    public static CliInvocationStreams Create(
        IReadOnlyList<string> args,
        IAnsiConsole inputConsole,
        TextWriter? commandOutput,
        TextWriter? commandError
    )
    {
        var output = commandOutput ?? Console.Out;
        var rawError = commandError ?? Console.Error;
        var isSbomCommand =
            CliOutputFormatOption.TryGetCommand(args, out var requestedCommand)
            && string.Equals(requestedCommand, "sbom", StringComparison.Ordinal);
        var routesDiagnosticsToError =
            isSbomCommand || CliOutputFormatOption.RoutesDiagnosticsToError(args);
        var diagnosticOutput = routesDiagnosticsToError
            ? new CliSanitizingTextWriter(rawError)
            : rawError;
        var diagnosticConsole = routesDiagnosticsToError
            ? CreateDiagnosticConsole(inputConsole, diagnosticOutput)
            : inputConsole;
        var jsonCommand = CliOutputFormatOption.TryGetJsonCommand(
            args,
            out var requestedJsonCommand
        )
            ? requestedJsonCommand
            : null;
        return new(
            output,
            rawError,
            diagnosticOutput,
            diagnosticConsole,
            routesDiagnosticsToError,
            isSbomCommand,
            jsonCommand
        );
    }

    private static CliRedirectedConsole CreateDiagnosticConsole(
        IAnsiConsole inputConsole,
        TextWriter output
    )
    {
        var outputConsole = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            }
        );
        return new CliRedirectedConsole(inputConsole, outputConsole);
    }
}
