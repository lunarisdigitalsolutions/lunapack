namespace Lunapack.Cli.Sources.Git;

internal static class GitSourceFailureFormatter
{
    public static string Format(string? sourceName, string? error, string fallback)
    {
        var message = error ?? fallback;
        if (
            string.IsNullOrWhiteSpace(sourceName)
            || !message.Contains("Git authentication failed", StringComparison.OrdinalIgnoreCase)
        )
        {
            return message;
        }

        return $"Unable to authenticate to Git source '{sourceName.Trim()}'. Configure the operating-system Git credential manager or SSH credentials, or run 'luna sources ignore {sourceName.Trim()}' to skip the source locally.";
    }
}
