namespace Lunapack.Cli.Application;

internal static class CliLogLevelParser
{
    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out CliLogLevel logLevel,
        out string? error
    )
    {
        logLevel = CliLogLevel.Info;
        error = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            string? value = null;
            var isSeparateOption =
                string.Equals(argument, "--log-level", StringComparison.Ordinal)
                || string.Equals(argument, "-ll", StringComparison.Ordinal);
            if (isSeparateOption)
            {
                if (index + 1 == arguments.Count)
                {
                    error = "Log level must be verbose, debug, info, warning, or error.";
                    return false;
                }

                value = arguments[++index];
            }
            else
            {
                var isInlineOption =
                    argument.StartsWith("--log-level=", StringComparison.Ordinal)
                    || argument.StartsWith("-ll=", StringComparison.Ordinal);
                if (isInlineOption)
                {
                    value = argument[(argument.IndexOf('=') + 1)..];
                }
            }

            if (value is null)
            {
                continue;
            }

            if (!TryParseValue(value, out logLevel))
            {
                error = "Log level must be verbose, debug, info, warning, or error.";
                return false;
            }
        }

        return true;
    }

    private static bool TryParseValue(string value, out CliLogLevel logLevel)
    {
        switch (value)
        {
            case "verbose":
                logLevel = CliLogLevel.Verbose;
                return true;
            case "debug":
                logLevel = CliLogLevel.Debug;
                return true;
            case "info":
                logLevel = CliLogLevel.Info;
                return true;
            case "warning":
                logLevel = CliLogLevel.Warning;
                return true;
            case "error":
                logLevel = CliLogLevel.Error;
                return true;
            default:
                logLevel = CliLogLevel.Info;
                return false;
        }
    }
}
