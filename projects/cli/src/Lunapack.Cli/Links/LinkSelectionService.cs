using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Lunapack.Cli.Links;

internal static class LinkSelectionService
{
    public static ManifestOperationResult<IReadOnlyList<string>> Select(
        string linkName,
        ProjectConfiguration.Link link,
        IReadOnlyList<string> candidatePaths
    )
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(candidatePaths);

        var basePath = NormalizeSelector(link.Path);
        var basePrefix = basePath.Length == 0 ? string.Empty : $"{basePath}/";
        var scopedPaths = candidatePaths
            .Select(ProjectPath.Normalize)
            .Where(path =>
                basePrefix.Length == 0 || path.StartsWith(basePrefix, StringComparison.Ordinal)
            )
            .ToList();

        var included = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var include in link.Includes)
        {
            var pattern = NormalizeSelector(include);
            if (pattern.Length == 0)
            {
                return Failure(linkName, "include patterns must not be empty");
            }

            var matches = Match(scopedPaths, basePrefix, pattern);
            if (matches.Count == 0)
            {
                return Failure(linkName, $"include '{include}' does not match any source file");
            }

            included.UnionWith(matches);
        }

        foreach (var exclude in link.Excludes)
        {
            var pattern = NormalizeSelector(exclude);
            if (pattern.Length == 0)
            {
                return Failure(linkName, "exclude patterns must not be empty");
            }

            included.ExceptWith(Match(scopedPaths, basePrefix, pattern));
        }

        return included.Count == 0
            ? Failure(linkName, "the include and exclude patterns select no files")
            : ManifestOperationResult<IReadOnlyList<string>>.Success([.. included]);
    }

    private static List<string> Match(
        IReadOnlyList<string> scopedPaths,
        string basePrefix,
        string pattern
    )
    {
        var directoryPrefix = $"{basePrefix}{pattern}/";
        var exactPath = $"{basePrefix}{pattern}";
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(pattern);

        return
        [
            .. scopedPaths.Where(path =>
                string.Equals(path, exactPath, StringComparison.Ordinal)
                || path.StartsWith(directoryPrefix, StringComparison.Ordinal)
                || matcher.Match(path[basePrefix.Length..]).HasMatches
            ),
        ];
    }

    private static ManifestOperationResult<IReadOnlyList<string>> Failure(
        string linkName,
        string reason
    ) => ManifestOperationResult<IReadOnlyList<string>>.Failure($"Link '{linkName}': {reason}.");

    private static string NormalizeSelector(string? selector) =>
        selector is null ? string.Empty : ProjectPath.Normalize(selector).Trim('/');
}
