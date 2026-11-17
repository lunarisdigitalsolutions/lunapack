using System.Text.RegularExpressions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;
using NuGet.Versioning;

namespace Lunapack.Cli.Sources.Oci;

internal static partial class OciRepositoryTagMapper
{
    private const int MaximumRepositoryLength = 255;
    private const int MaximumTagLength = 128;

    public static ManifestOperationResult<string> CreateRepository(
        ProjectConfiguration.OciSource source,
        string packId
    )
    {
        var location = SourceIdentityNormalizer.NormalizeOciLocation(source.Url, source.Path);
        if (!location.IsSuccess)
        {
            return ManifestOperationResult<string>.Failure(
                location.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        var component = SourceIdentityNormalizer.ValidateOciRepositoryComponent(packId);
        if (component.Value is null)
        {
            return ManifestOperationResult<string>.Failure(
                component.Error ?? $"Pack ID '{packId}' cannot be represented as an OCI repository."
            );
        }

        var repository = string.IsNullOrEmpty(location.Value.Path)
            ? component.Value
            : $"{location.Value.Path}/{component.Value}";
        return repository.Length <= MaximumRepositoryLength
            ? ManifestOperationResult<string>.Success(repository)
            : ManifestOperationResult<string>.Failure(
                "The OCI repository name exceeds 255 characters."
            );
    }

    public static ManifestOperationResult<string> CreateTag(string version)
    {
        if (!NuGetVersion.TryParse(version, out _))
        {
            return ManifestOperationResult<string>.Failure(
                $"Pack version '{version}' is not a valid semantic version."
            );
        }

        var tag = version.Replace('+', '_');
        return tag.Length <= MaximumTagLength && OciTagPattern().IsMatch(tag)
            ? ManifestOperationResult<string>.Success(tag)
            : ManifestOperationResult<string>.Failure(
                $"Pack version '{version}' cannot be represented as an OCI tag."
            );
    }

    public static ManifestOperationResult<string> ReadVersion(string tag)
    {
        if (!OciTagPattern().IsMatch(tag))
        {
            return ManifestOperationResult<string>.Failure(
                $"OCI tag '{tag}' is not a supported LunaPack version."
            );
        }

        var version = tag.Replace('_', '+');
        var mappedTag = CreateTag(version);
        return
            mappedTag.Value is { } canonicalTag
            && string.Equals(canonicalTag, tag, StringComparison.Ordinal)
            ? ManifestOperationResult<string>.Success(version)
            : ManifestOperationResult<string>.Failure(
                $"OCI tag '{tag}' is not a supported LunaPack version."
            );
    }

    public static ManifestOperationResult<string> ReadPackId(string repository, string? sourcePath)
    {
        var normalizedPath = ProjectPath.NormalizeOptional(sourcePath)?.Trim('/');
        var packId =
            normalizedPath is null ? repository
            : repository.StartsWith($"{normalizedPath}/", StringComparison.Ordinal)
                ? repository[(normalizedPath.Length + 1)..]
            : string.Empty;
        if (packId.Length == 0 || packId.Contains('/'))
        {
            return ManifestOperationResult<string>.Failure(
                $"Repository '{repository}' is outside the configured OCI source path."
            );
        }

        var validComponent = SourceIdentityNormalizer.ValidateOciRepositoryComponent(packId);
        return validComponent.Value is { } component
            ? ManifestOperationResult<string>.Success(component)
            : ManifestOperationResult<string>.Failure(
                validComponent.Error ?? $"Repository '{repository}' has an invalid pack ID."
            );
    }

    [GeneratedRegex(
        @"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex OciTagPattern();
}
