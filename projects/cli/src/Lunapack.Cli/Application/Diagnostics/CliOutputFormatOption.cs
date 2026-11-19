using System.CommandLine;

namespace Lunapack.Cli.Application.Diagnostics;

internal static class CliOutputFormatOption
{
    public static Option<string?> Create()
    {
        var option = new Option<string?>("--format")
        {
            Description = "Output format: text or json.",
        };
        option.CompletionSources.Add("text", "json");
        return option;
    }

    public static bool TryParse(string? value, out CliOutputFormat format)
    {
        if (value is null || string.Equals(value, "text", StringComparison.OrdinalIgnoreCase))
        {
            format = CliOutputFormat.Text;
            return true;
        }

        if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
        {
            format = CliOutputFormat.Json;
            return true;
        }

        format = default;
        return false;
    }

    public static string FormatUnsupportedMessage(string? value) =>
        $"Unsupported output format '{value}'. Expected 'text' or 'json'.";

    public static bool RoutesDiagnosticsToError(IReadOnlyList<string> args) =>
        TryReadFormat(args, out var value) && !IsText(value);

    public static bool TryGetJsonCommand(IReadOnlyList<string> args, out string command)
    {
        command = string.Empty;
        if (!TryReadFormat(args, out var value) || !IsJson(value))
        {
            return false;
        }

        return TryReadCommand(args, out command);
    }

    private static bool IsJson(string? value) =>
        string.Equals(value, "json", StringComparison.OrdinalIgnoreCase);

    private static bool IsText(string? value) =>
        value is not null && string.Equals(value, "text", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadCommand(IReadOnlyList<string> args, out string command)
    {
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (
                string.Equals(argument, "--format", StringComparison.Ordinal)
                || string.Equals(argument, "--workspace", StringComparison.Ordinal)
                || string.Equals(argument, "-w", StringComparison.Ordinal)
                || string.Equals(argument, "--log-level", StringComparison.Ordinal)
                || string.Equals(argument, "-ll", StringComparison.Ordinal)
            )
            {
                index++;
                continue;
            }

            if (
                argument.StartsWith("--format=", StringComparison.Ordinal)
                || argument.StartsWith("--workspace=", StringComparison.Ordinal)
                || argument.StartsWith("-w=", StringComparison.Ordinal)
                || argument.StartsWith("--log-level=", StringComparison.Ordinal)
                || argument.StartsWith("-ll=", StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (argument.StartsWith('-'))
            {
                continue;
            }

            command = argument;
            return command is "audit" or "outdated" or "validate";
        }

        command = string.Empty;
        return false;
    }

    private static bool TryReadFormat(IReadOnlyList<string> args, out string? value)
    {
        value = null;
        var found = false;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--format", StringComparison.Ordinal))
            {
                found = true;
                value = index + 1 < args.Count ? args[index + 1] : null;
                index++;
                continue;
            }

            const string prefix = "--format=";
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                found = true;
                value = argument[prefix.Length..];
            }
        }

        return found;
    }
}
