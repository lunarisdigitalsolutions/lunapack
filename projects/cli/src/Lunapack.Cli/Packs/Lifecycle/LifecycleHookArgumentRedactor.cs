namespace Lunapack.Cli.Packs.Lifecycle;

internal static class LifecycleHookArgumentRedactor
{
    public const string RedactedValue = "[REDACTED]";

    private static readonly string[] _secretNameFragments =
    [
        "password",
        "passwd",
        "secret",
        "token",
        "credential",
        "authorization",
        "auth",
        "apikey",
        "api-key",
        "api_key",
        "accesskey",
        "access-key",
        "access_key",
    ];

    public static IReadOnlyList<string> Redact(IReadOnlyList<string> arguments)
    {
        var sanitized = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            var assignmentIndex = argument.IndexOf('=');
            if (assignmentIndex >= 0)
            {
                var name = argument[..assignmentIndex];
                sanitized.Add(
                    IsSecretName(name)
                        ? $"{argument[..(assignmentIndex + 1)]}{RedactedValue}"
                        : argument
                );
                continue;
            }

            sanitized.Add(argument);
            if (IsSecretName(argument) && index + 1 < arguments.Count)
            {
                sanitized.Add(RedactedValue);
                index++;
            }
        }

        return sanitized;
    }

    private static bool IsSecretName(string name) =>
        _secretNameFragments.Any(fragment =>
            name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
        );
}
