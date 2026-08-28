using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed class LinkTargetMapper(IFileSystem fileSystem)
{
    public ManifestOperationResult<IReadOnlyList<LinkFileMapping>> Map(
        string projectDirectory,
        string linkName,
        ProjectConfiguration.Link link,
        IReadOnlyList<string> selectedSourcePaths,
        ManagedFileTargetRemapping? targetRemapping = null,
        ManagedFileTargetRemapping? configuredRemapping = null,
        IReadOnlyDictionary<string, string>? retainedTargets = null
    )
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(selectedSourcePaths);

        var mappings = new List<LinkFileMapping>(selectedSourcePaths.Count);
        var claimedTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        var target = NormalizeSelector(link.Target);
        foreach (var sourcePath in selectedSourcePaths)
        {
            var mappedPath = MapRelativePath(linkName, link, sourcePath);
            if (mappedPath.Value is not { } relativeTarget)
            {
                return Failure(mappedPath.Error);
            }

            var normalizedTarget = ProjectPath.NormalizeProjectRelativePath(
                fileSystem,
                projectDirectory,
                target.Length == 0 ? relativeTarget : $"{target}/{relativeTarget}"
            );
            if (normalizedTarget.Value is not { } effectiveTarget)
            {
                return Failure($"Link '{linkName}': {normalizedTarget.Error}");
            }

            var configuredTarget =
                targetRemapping?.Resolve(effectiveTarget, configuredRemapping)
                ?? configuredRemapping?.Resolve(effectiveTarget)
                ?? effectiveTarget;
            if (
                string.Equals(
                    configuredTarget,
                    ManagedFileTargetRemapping.IgnoreTarget,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            var remappedTarget = retainedTargets?.GetValueOrDefault(sourcePath) ?? configuredTarget;
            if (claimedTargets.TryGetValue(remappedTarget, out var claimingSourcePath))
            {
                return Failure(
                    $"Link '{linkName}': '{sourcePath}' and '{claimingSourcePath}' both map to '{remappedTarget}'."
                );
            }

            claimedTargets.Add(remappedTarget, sourcePath);
            mappings.Add(new LinkFileMapping(sourcePath, effectiveTarget, remappedTarget));
        }

        return ManifestOperationResult<IReadOnlyList<LinkFileMapping>>.Success(mappings);
    }

    private static ManifestOperationResult<string> MapRelativePath(
        string linkName,
        ProjectConfiguration.Link link,
        string sourcePath
    )
    {
        var linkRelativePath = sourcePath[CreatePrefix(link.Path).Length..];
        var stripPrefix = CreatePrefix(link.StripPrefix);
        if (stripPrefix.Length > 0)
        {
            if (!linkRelativePath.StartsWith(stripPrefix, StringComparison.Ordinal))
            {
                return ManifestOperationResult<string>.Failure(
                    $"Link '{linkName}': strip prefix '{link.StripPrefix}' is not a complete prefix of '{linkRelativePath}'."
                );
            }

            linkRelativePath = linkRelativePath[stripPrefix.Length..];
        }

        var mappedPath = link.Flatten is true
            ? linkRelativePath[(linkRelativePath.LastIndexOf('/') + 1)..]
            : linkRelativePath;

        return mappedPath.Length == 0
            ? ManifestOperationResult<string>.Failure(
                $"Link '{linkName}': '{sourcePath}' maps to an empty target path."
            )
            : ManifestOperationResult<string>.Success(mappedPath);
    }

    private static ManifestOperationResult<IReadOnlyList<LinkFileMapping>> Failure(string? error) =>
        ManifestOperationResult<IReadOnlyList<LinkFileMapping>>.Failure(
            error ?? "Unable to map link targets."
        );

    private static string CreatePrefix(string? path)
    {
        var normalized = NormalizeSelector(path);
        return normalized.Length == 0 ? string.Empty : $"{normalized}/";
    }

    private static string NormalizeSelector(string? path) =>
        path is null ? string.Empty : ProjectPath.Normalize(path).Trim('/');
}
