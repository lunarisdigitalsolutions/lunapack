namespace Lunapack.Cli.Sources;

internal static class GitHubShorthand
{
    public static bool TryCreateUrl(string repository, out string repositoryUrl)
    {
        var segments = repository.Split('/', StringSplitOptions.None);
        var hasValidCoordinate = segments.Length == 2 && segments.All(IsValidSegment);
        if (!hasValidCoordinate)
        {
            repositoryUrl = string.Empty;
            return false;
        }

        repositoryUrl = $"https://github.com/{repository}.git";
        return true;
    }

    private static bool IsValidSegment(string segment) =>
        !string.IsNullOrEmpty(segment)
        && segment.All(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
        );
}
