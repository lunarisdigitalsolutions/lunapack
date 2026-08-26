using System.Text.RegularExpressions;

namespace Lunapack.Cli;

internal static partial class SourceIdentityNormalizer
{
    private const string DefaultBasePath = "/";

    private static readonly HashSet<string> _supportedSchemes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "https",
        "http",
        "ssh",
        "git",
    };

    public static ManifestOperationResult<SourceFingerprint> CreateGit(
        string url,
        string? reference,
        string? path
    )
    {
        var identity = NormalizeRepository(url);
        if (identity.Value is not { } repository)
        {
            return ManifestOperationResult<SourceFingerprint>.Failure(
                identity.Error ?? $"Unable to normalize repository URL '{url}'."
            );
        }

        var basePath = NormalizeBasePath(path);
        if (basePath.Value is not { } normalizedPath)
        {
            return ManifestOperationResult<SourceFingerprint>.Failure(
                basePath.Error ?? $"Unable to normalize source path '{path}'."
            );
        }

        return ManifestOperationResult<SourceFingerprint>.Success(
            new SourceFingerprint
            {
                Type = SourceFingerprint.GitType,
                Identity = repository,
                Ref = NormalizeRef(reference),
                Path = normalizedPath,
            }
        );
    }

    public static SourceFingerprint CreateLocal(string canonicalPath) =>
        new()
        {
            Type = SourceFingerprint.LocalType,
            Identity = ProjectPath.Normalize(canonicalPath).TrimEnd('/'),
        };

    public static ManifestOperationResult<SourceFingerprint> Create(
        ConfiguredSourceIdentity identity
    ) =>
        string.Equals(identity.Type, SourceFingerprint.LocalType, StringComparison.Ordinal)
            ? ManifestOperationResult<SourceFingerprint>.Success(
                CreateLocal(identity.Path ?? string.Empty)
            )
            : CreateGit(identity.Url ?? string.Empty, identity.Ref, identity.Path);

    public static ManifestOperationResult<SourceFingerprint> Create(
        ProjectConfiguration.Source source
    ) =>
        source switch
        {
            ProjectConfiguration.LocalSource localSource =>
                ManifestOperationResult<SourceFingerprint>.Success(CreateLocal(localSource.Path)),
            ProjectConfiguration.GitSource gitSource => CreateGit(
                gitSource.Url,
                gitSource.Ref,
                gitSource.Path
            ),
            _ => ManifestOperationResult<SourceFingerprint>.Failure(
                "Unsupported configured source type."
            ),
        };

    public static string NormalizeRef(string? reference)
    {
        var trimmed = reference?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        return GitCommitPattern().IsMatch(trimmed) ? trimmed.ToLowerInvariant() : trimmed;
    }

    public static ManifestOperationResult<string> NormalizeBasePath(string? path)
    {
        var normalized = ProjectPath.NormalizeOptional(path)?.Trim();
        if (
            string.IsNullOrEmpty(normalized)
            || string.Equals(normalized, ".", StringComparison.Ordinal)
        )
        {
            return ManifestOperationResult<string>.Success(DefaultBasePath);
        }

        var segments = new List<string>();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(segment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return ManifestOperationResult<string>.Failure(
                    $"Source path '{path}' must not traverse outside the repository."
                );
            }

            segments.Add(segment);
        }

        return ManifestOperationResult<string>.Success(
            segments.Count == 0 ? DefaultBasePath : $"/{string.Join('/', segments)}"
        );
    }

    public static ManifestOperationResult<string> NormalizeRepository(string url)
    {
        var trimmed = url?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return ManifestOperationResult<string>.Failure("Repository URL is required.");
        }

        var parsed = ParseLocation(trimmed);
        if (parsed.Value is not { } location)
        {
            return ManifestOperationResult<string>.Failure(
                parsed.Error ?? $"Repository URL '{url}' is not a supported Git URL."
            );
        }

        if (
            location.UserInformation is { } credentials
            && credentials.Contains(':', StringComparison.Ordinal)
        )
        {
            return ManifestOperationResult<string>.Failure(
                "Repository URL must not embed credentials."
            );
        }

        var host = location.Host.ToLowerInvariant();
        var segments = ProjectPath
            .Normalize(location.Path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return ManifestOperationResult<string>.Failure(
                $"Repository URL '{url}' does not identify a repository."
            );
        }

        segments[^1] = TrimGitSuffix(segments[^1]);
        if (segments[^1].Length == 0)
        {
            return ManifestOperationResult<string>.Failure(
                $"Repository URL '{url}' does not identify a repository."
            );
        }

        var identityPath = string.Join('/', segments);
        if (IsCaseInsensitiveForge(host))
        {
            identityPath = identityPath.ToLowerInvariant();
        }

        return ManifestOperationResult<string>.Success($"{host}/{identityPath}");
    }

    private static ManifestOperationResult<RepositoryLocation> ParseLocation(string url)
    {
        var scpMatch = ScpLikePattern().Match(url);
        if (scpMatch.Success)
        {
            return ManifestOperationResult<RepositoryLocation>.Success(
                new RepositoryLocation(
                    scpMatch.Groups["user"].Success ? scpMatch.Groups["user"].Value : null,
                    scpMatch.Groups["host"].Value,
                    scpMatch.Groups["path"].Value
                )
            );
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return ManifestOperationResult<RepositoryLocation>.Failure(
                $"Repository URL '{url}' must be an absolute https, ssh, or scp-style Git URL."
            );
        }

        if (!_supportedSchemes.Contains(uri.Scheme))
        {
            return ManifestOperationResult<RepositoryLocation>.Failure(
                $"Repository URL '{url}' uses unsupported scheme '{uri.Scheme}'."
            );
        }

        var host =
            uri.IsDefaultPort || uri.Port <= 0
                ? uri.Host
                : $"{uri.Host}:{uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return ManifestOperationResult<RepositoryLocation>.Success(
            new RepositoryLocation(
                string.IsNullOrEmpty(uri.UserInfo) ? null : uri.UserInfo,
                host,
                uri.AbsolutePath
            )
        );
    }

    private sealed record RepositoryLocation(string? UserInformation, string Host, string Path);

    private static bool IsCaseInsensitiveForge(string host) =>
        string.Equals(host, "github.com", StringComparison.Ordinal)
        || host.EndsWith(".github.com", StringComparison.Ordinal);

    private static string TrimGitSuffix(string segment) =>
        segment.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segment[..^4] : segment;

    [GeneratedRegex(
        @"^(?:(?<user>[^@/\\:]+)@)?(?<host>[A-Za-z0-9._-]+):(?!//)(?<path>.+)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex ScpLikePattern();

    [GeneratedRegex(
        @"^[A-Fa-f0-9]{40}(?:[A-Fa-f0-9]{24})?$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex GitCommitPattern();
}
