namespace Lunapack.Cli.Sources;

internal static class GitHubShorthand
{
    public static bool TryCreateUrl(string repository, out string repositoryUrl)
    {
        var segments = repository.Split('/', StringSplitOptions.None);
        if (
            segments.Length != 2
            || segments.Any(segment =>
                string.IsNullOrEmpty(segment)
                || segment.Any(character =>
                    !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'
                )
            )
        )
        {
            repositoryUrl = string.Empty;
            return false;
        }

        repositoryUrl = $"https://github.com/{repository}.git";
        return true;
    }
}
