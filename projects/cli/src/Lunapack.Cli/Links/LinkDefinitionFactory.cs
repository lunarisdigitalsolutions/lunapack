using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Links;

internal sealed class LinkDefinitionFactory(IFileSystem fileSystem)
{
    public ManifestOperationResult<ProjectConfiguration.Link> Create(
        string projectDirectory,
        ProjectState state,
        string name,
        LinkDefinitionRequest request,
        bool force
    )
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);

        var includes = NormalizeSelectors(request.Includes);
        var excludes = NormalizeSelectors(request.Excludes);
        if (ValidateRequest(state, name, request, includes, excludes, force) is { } requestError)
        {
            return ManifestOperationResult<ProjectConfiguration.Link>.Failure(requestError);
        }

        if (request.Source is not { Length: > 0 } source)
        {
            return ManifestOperationResult<ProjectConfiguration.Link>.Failure(
                "A source name is required. Use --source <name>."
            );
        }

        var basePath = NormalizeSourcePath(request.Path, "--path");
        var stripPrefix = NormalizeSourcePath(request.StripPrefix, "--strip-prefix");
        var target = NormalizeTarget(projectDirectory, request.Target);
        if (
            new[] { basePath, stripPrefix, target }.FirstOrDefault(result => !result.IsSuccess) is
            { } failure
        )
        {
            return ManifestOperationResult<ProjectConfiguration.Link>.Failure(
                failure.Error ?? "Unable to normalize the link definition paths."
            );
        }

        return ManifestOperationResult<ProjectConfiguration.Link>.Success(
            new ProjectConfiguration.Link
            {
                Source = source,
                Includes = [.. includes],
                Excludes = [.. excludes],
                Path = basePath.Value,
                Target = target.Value,
                Ref = request.Ref,
                StripPrefix = stripPrefix.Value,
                Flatten = request.Flatten ? true : null,
            }
        );
    }

    private static string? ValidateRequest(
        ProjectState state,
        string name,
        LinkDefinitionRequest request,
        List<string> includes,
        List<string> excludes,
        bool force
    )
    {
        if (ValidateName(state, name, force) is { } nameError)
        {
            return nameError;
        }

        if (string.IsNullOrEmpty(request.Source))
        {
            return "A source name is required. Use --source <name>.";
        }

        if (
            !state.Configuration.Sources.Exists(source =>
                string.Equals(source.Name, request.Source, StringComparison.Ordinal)
            )
        )
        {
            return $"Source '{request.Source}' is not configured.";
        }

        return ValidateSelectors(includes, excludes)
            ?? (request.Ref is { Length: 0 } ? "A Git ref must not be empty." : null);
    }

    private static string? ValidateName(ProjectState state, string name, bool force)
    {
        if (!ManifestModelValidator.IsPackId(name))
        {
            return $"Link name '{name}' must use pack-ID syntax.";
        }

        if (!force && state.Configuration.Links.ContainsKey(name))
        {
            return $"Link '{name}' already exists. Use --force to replace its definition.";
        }

        var conflictsWithPack =
            state.Configuration.Packs.Exists(pack =>
                string.Equals(pack.Id, name, StringComparison.Ordinal)
            )
            || state.LockFile.Packs.Exists(pack =>
                string.Equals(pack.Id, name, StringComparison.Ordinal)
            );
        return conflictsWithPack ? $"Link name '{name}' is already used by pack '{name}'." : null;
    }

    private static string? ValidateSelectors(List<string> includes, List<string> excludes)
    {
        if (includes.Count == 0)
        {
            return "At least one include selector is required. Use --include <pattern>.";
        }

        var unsafeSelector = includes
            .Concat(excludes)
            .FirstOrDefault(selector => !IsSafeSelector(selector));
        return unsafeSelector is null
            ? null
            : $"Selector '{unsafeSelector}' must be a relative path inside the source.";
    }

    private static List<string> NormalizeSelectors(IReadOnlyList<string> selectors) =>
        [
            .. selectors
                .Where(selector => !string.IsNullOrWhiteSpace(selector))
                .Select(selector => ProjectPath.Normalize(selector).Trim('/'))
                .Where(selector => selector.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];

    private static bool IsSafeSelector(string selector) =>
        !selector.StartsWith('/')
        && !(selector.Length >= 2 && char.IsAsciiLetter(selector[0]) && selector[1] == ':')
        && !selector.Split('/', StringSplitOptions.None).Contains("..", StringComparer.Ordinal);

    private static ManifestOperationResult<string?> NormalizeSourcePath(
        string? path,
        string optionName
    )
    {
        if (path is null)
        {
            return ManifestOperationResult<string?>.Success(null);
        }

        var normalizedPath = ProjectPath.Normalize(path).Trim('/');
        return normalizedPath.Length == 0 || !IsSafeSelector(normalizedPath)
            ? ManifestOperationResult<string?>.Failure(
                $"{optionName} must be a relative path inside the source."
            )
            : ManifestOperationResult<string?>.Success(normalizedPath);
    }

    private ManifestOperationResult<string?> NormalizeTarget(
        string projectDirectory,
        string? target
    )
    {
        if (target is null)
        {
            return ManifestOperationResult<string?>.Success(null);
        }

        var normalizedTarget = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            target
        );
        if (normalizedTarget.Value is not { } projectRelativeTarget)
        {
            return ManifestOperationResult<string?>.Failure(
                $"--target must be a relative path inside the workspace. {normalizedTarget.Error}"
            );
        }

        return ManifestOperationResult<string?>.Success(
            projectRelativeTarget.Length == 0
            || string.Equals(projectRelativeTarget, ".", StringComparison.Ordinal)
                ? null
                : projectRelativeTarget
        );
    }
}
