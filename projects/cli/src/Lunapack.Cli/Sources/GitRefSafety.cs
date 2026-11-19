namespace Lunapack.Cli.Sources;

internal static class GitRefSafety
{
    public static bool IsSafe(string? reference)
    {
        if (
            string.IsNullOrWhiteSpace(reference)
            || string.Equals(reference, "@", StringComparison.Ordinal)
            || reference.Any(character =>
                char.IsWhiteSpace(character)
                || char.IsControl(character)
                || character is '?' or '#' or '\\' or '~' or '^' or ':' or '*' or '['
            )
            || reference.Contains("..", StringComparison.Ordinal)
            || reference.Contains("@{", StringComparison.Ordinal)
            || reference.StartsWith('/')
            || reference.EndsWith('/')
            || reference.EndsWith('.')
            || reference.Contains("//", StringComparison.Ordinal)
        )
        {
            return false;
        }

        return reference
            .Split('/')
            .All(segment =>
                segment.Length > 0
                && !segment.StartsWith('.')
                && !string.Equals(segment, ".", StringComparison.Ordinal)
                && !string.Equals(segment, "..", StringComparison.Ordinal)
                && !segment.EndsWith(".lock", StringComparison.Ordinal)
            );
    }
}
