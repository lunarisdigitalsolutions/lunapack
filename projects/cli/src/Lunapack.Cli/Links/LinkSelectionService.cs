using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Project;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Lunapack.Cli.Links;

internal static class LinkSelectionService
{
    public static ManifestOperationResult<IReadOnlyList<string>> Select(
        string linkName,
        ProjectConfiguration.Link link,
        IReadOnlyList<string> candidatePaths,
        ResourceLimits? resourceLimits = null
    )
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(candidatePaths);

        var basePath = NormalizeSelector(link.Path);
        var basePrefix = basePath.Length == 0 ? string.Empty : $"{basePath}/";
        var selectors = CreateSelectors(linkName, link, basePrefix);
        if (selectors.Value is not { } selectionSelectors)
        {
            return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                selectors.Error!,
                selectors.ErrorKind
            );
        }

        var includeSelectors = selectionSelectors.Includes;
        var excludeSelectors = selectionSelectors.Excludes;
        var includesMatched = new bool[includeSelectors.Count];
        var included = new SortedSet<string>(StringComparer.Ordinal);
        var budget = new LinkSnapshotBudget(resourceLimits ?? ResourceLimits.Default);
        foreach (var candidatePath in candidatePaths)
        {
            var path = ProjectPath.Normalize(candidatePath);
            if (basePrefix.Length > 0 && !path.StartsWith(basePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var selected = false;
            for (var index = 0; index < includeSelectors.Count; index++)
            {
                if (includeSelectors[index].Selector.Matches(path))
                {
                    includesMatched[index] = true;
                    selected = true;
                }
            }

            if (!selected || excludeSelectors.Any(selector => selector.Matches(path)))
            {
                continue;
            }

            if (included.Add(path))
            {
                var observed = budget.AddFilePath(path, 0);
                if (!observed.IsSuccess)
                {
                    return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                        observed.Error!,
                        observed.ErrorKind
                    );
                }
            }
        }

        for (var index = 0; index < includeSelectors.Count; index++)
        {
            if (!includesMatched[index])
            {
                return Failure(
                    linkName,
                    $"include '{includeSelectors[index].Pattern}' does not match any source file"
                );
            }
        }

        return included.Count == 0
            ? Failure(linkName, "the include and exclude patterns select no files")
            : ManifestOperationResult<IReadOnlyList<string>>.Success([.. included]);
    }

    private static ManifestOperationResult<SelectionSelectors> CreateSelectors(
        string linkName,
        ProjectConfiguration.Link link,
        string basePrefix
    )
    {
        var includes = new List<(string Pattern, PathSelector Selector)>();
        foreach (var include in link.Includes)
        {
            var pattern = NormalizeSelector(include);
            if (pattern.Length == 0)
            {
                return ManifestOperationResult<SelectionSelectors>.Failure(
                    $"Link '{linkName}': include patterns must not be empty."
                );
            }

            includes.Add((include, new PathSelector(basePrefix, pattern)));
        }

        var excludes = new List<PathSelector>();
        foreach (var exclude in link.Excludes)
        {
            var pattern = NormalizeSelector(exclude);
            if (pattern.Length == 0)
            {
                return ManifestOperationResult<SelectionSelectors>.Failure(
                    $"Link '{linkName}': exclude patterns must not be empty."
                );
            }

            excludes.Add(new PathSelector(basePrefix, pattern));
        }

        return ManifestOperationResult<SelectionSelectors>.Success(
            new SelectionSelectors(includes, excludes)
        );
    }

    private sealed record SelectionSelectors(
        IReadOnlyList<(string Pattern, PathSelector Selector)> Includes,
        IReadOnlyList<PathSelector> Excludes
    );

    private sealed class PathSelector
    {
        private readonly string _basePrefix;
        private readonly string _directoryPrefix;
        private readonly string _exactPath;
        private readonly Matcher _matcher;

        public PathSelector(string basePrefix, string pattern)
        {
            _basePrefix = basePrefix;
            _directoryPrefix = $"{basePrefix}{pattern}/";
            _exactPath = $"{basePrefix}{pattern}";
            _matcher = new Matcher(StringComparison.Ordinal);
            _matcher.AddInclude(pattern);
        }

        public bool Matches(string path) =>
            string.Equals(path, _exactPath, StringComparison.Ordinal)
            || path.StartsWith(_directoryPrefix, StringComparison.Ordinal)
            || _matcher.Match(path[_basePrefix.Length..]).HasMatches;
    }

    private static ManifestOperationResult<IReadOnlyList<string>> Failure(
        string linkName,
        string reason
    ) => ManifestOperationResult<IReadOnlyList<string>>.Failure($"Link '{linkName}': {reason}.");

    private static string NormalizeSelector(string? selector) =>
        selector is null ? string.Empty : ProjectPath.Normalize(selector).Trim('/');
}
