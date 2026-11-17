using System.Text.RegularExpressions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Sources;

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

    public static ManifestOperationResult<SourceFingerprint> CreateOci(string url, string? path)
    {
        var location = NormalizeOciLocation(url, path);
        if (!location.IsSuccess)
        {
            return ManifestOperationResult<SourceFingerprint>.Failure(
                location.Error ?? "OCI source location is invalid."
            );
        }

        var normalized = location.Value;
        return ManifestOperationResult<SourceFingerprint>.Success(
            new SourceFingerprint
            {
                Type = SourceFingerprint.OciType,
                Identity = normalized.Url,
                Path = normalized.Path,
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
        identity.Type switch
        {
            SourceFingerprint.LocalType => ManifestOperationResult<SourceFingerprint>.Success(
                CreateLocal(identity.Path ?? string.Empty)
            ),
            SourceFingerprint.GitType => CreateGit(
                identity.Url ?? string.Empty,
                identity.Ref,
                identity.Path
            ),
            SourceFingerprint.OciType => CreateOci(identity.Url ?? string.Empty, identity.Path),
            _ => ManifestOperationResult<SourceFingerprint>.Failure(
                "Unsupported configured source type."
            ),
        };

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
            ProjectConfiguration.OciSource ociSource => CreateOci(ociSource.Url, ociSource.Path),
            _ => ManifestOperationResult<SourceFingerprint>.Failure(
                "Unsupported configured source type."
            ),
        };

    public static ManifestOperationResult<(string Url, string? Path)> NormalizeOciLocation(
        string url,
        string? path
    )
    {
        var parsedUrl = ParseOciRegistryUrl(url);
        if (!parsedUrl.IsSuccess)
        {
            return ManifestOperationResult<(string Url, string? Path)>.Failure(
                parsedUrl.Error ?? "OCI registry URL is invalid."
            );
        }

        if (parsedUrl.Value is not { } registryUrl)
        {
            return ManifestOperationResult<(string Url, string? Path)>.Failure(
                "OCI registry URL is invalid."
            );
        }

        var repositoryPath = NormalizeOciLocationPath(registryUrl.RawPath, path);
        if (!repositoryPath.IsSuccess)
        {
            return ManifestOperationResult<(string Url, string? Path)>.Failure(
                repositoryPath.Error ?? "OCI repository path is invalid."
            );
        }

        return ManifestOperationResult<(string Url, string? Path)>.Success(
            (NormalizeOciOrigin(registryUrl.Uri), repositoryPath.Value)
        );
    }

    public static ManifestOperationResult<string> ValidateOciRepositoryComponent(
        string component
    ) =>
        OciRepositoryComponentPattern().IsMatch(component)
            ? ManifestOperationResult<string>.Success(component)
            : ManifestOperationResult<string>.Failure(
                "OCI repository components must be lowercase and use valid OCI characters."
            );

    private static ManifestOperationResult<OciRegistryUrl> ParseOciRegistryUrl(string url)
    {
        var trimmedUrl = url?.Trim() ?? string.Empty;
        if (trimmedUrl.Length == 0)
        {
            return ManifestOperationResult<OciRegistryUrl>.Failure("OCI registry URL is required.");
        }

        if (
            trimmedUrl.Contains('\\')
            || !Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || uri.HostNameType
                is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6)
        )
        {
            return ManifestOperationResult<OciRegistryUrl>.Failure(
                "OCI registry URL must be an absolute HTTPS URL."
            );
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ManifestOperationResult<OciRegistryUrl>.Failure(
                "OCI registry URLs must use HTTPS."
            );
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return ManifestOperationResult<OciRegistryUrl>.Failure(
                "OCI registry URLs must not contain credentials."
            );
        }

        if (
            uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || trimmedUrl.Contains('?')
            || trimmedUrl.Contains('#')
        )
        {
            return ManifestOperationResult<OciRegistryUrl>.Failure(
                "OCI registry URLs must not contain a query or fragment."
            );
        }

        if (uri.Port is < 1 or > 65535)
        {
            return ManifestOperationResult<OciRegistryUrl>.Failure(
                "OCI registry URL has an invalid port."
            );
        }

        return ManifestOperationResult<OciRegistryUrl>.Success(
            new OciRegistryUrl(uri, GetRawUriPath(trimmedUrl))
        );
    }

    private static ManifestOperationResult<string?> NormalizeOciLocationPath(
        string rawEmbeddedPath,
        string? path
    )
    {
        var normalizedEmbeddedPath = NormalizeOciPath(
            rawEmbeddedPath.Length == 0 ? null : rawEmbeddedPath[1..]
        );
        var normalizedExplicitPath = NormalizeOciPath(path);
        if (!normalizedEmbeddedPath.IsSuccess || !normalizedExplicitPath.IsSuccess)
        {
            return ManifestOperationResult<string?>.Failure(
                "OCI repository paths must be relative lowercase OCI paths without traversal."
            );
        }

        var hasExplicitPath = !string.IsNullOrWhiteSpace(path);
        if (
            hasExplicitPath
            && !string.Equals(
                normalizedExplicitPath.Value,
                normalizedEmbeddedPath.Value,
                StringComparison.Ordinal
            )
            && normalizedEmbeddedPath.Value is not null
        )
        {
            return ManifestOperationResult<string?>.Failure(
                "OCI registry URL path conflicts with the configured repository path."
            );
        }

        return ManifestOperationResult<string?>.Success(
            hasExplicitPath ? normalizedExplicitPath.Value : normalizedEmbeddedPath.Value
        );
    }

    private static string NormalizeOciOrigin(Uri uri)
    {
        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.IPv6 && (host.Length == 0 || host[0] != '['))
        {
            host = $"[{host}]";
        }

        return new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Host = host,
            UserName = string.Empty,
            Password = string.Empty,
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
            Port = uri.Port == 443 ? -1 : uri.Port,
        }
            .Uri.GetLeftPart(UriPartial.Authority)
            .TrimEnd('/');
    }

    private static ManifestOperationResult<string?> NormalizeOciPath(string? path)
    {
        var normalized = ProjectPath.NormalizeOptional(path)?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return ManifestOperationResult<string?>.Success(null);
        }

        if (
            normalized[0] == '/'
            || (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
        )
        {
            return ManifestOperationResult<string?>.Failure(
                "OCI repository paths must be relative."
            );
        }

        var segments = new List<string>();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(segment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (
                string.Equals(segment, "..", StringComparison.Ordinal)
                || !OciRepositoryComponentPattern().IsMatch(segment)
            )
            {
                return ManifestOperationResult<string?>.Failure(
                    "OCI repository paths must contain valid lowercase components."
                );
            }

            segments.Add(segment);
        }

        return NormalizeOciPathSegments(segments);
    }

    private static ManifestOperationResult<string?> NormalizeOciPathSegments(List<string> segments)
    {
        if (segments.Count == 0)
        {
            return ManifestOperationResult<string?>.Success(null);
        }

        var path = string.Join('/', segments);
        return path.Length <= 255
            ? ManifestOperationResult<string?>.Success(path)
            : ManifestOperationResult<string?>.Failure(
                "OCI repository paths must not exceed 255 characters."
            );
    }

    private static string GetRawUriPath(string url)
    {
        var authorityStart = url.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return string.Empty;
        }

        authorityStart += 3;
        var pathStart = url.IndexOfAny(['/', '?', '#'], authorityStart);
        if (pathStart < 0 || url[pathStart] != '/')
        {
            return string.Empty;
        }

        var pathEnd = url.IndexOfAny(['?', '#'], pathStart);
        return pathEnd < 0 ? url[pathStart..] : url[pathStart..pathEnd];
    }

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
            normalized is null
            || normalized.Length == 0
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

        if (trimmed[0] == '/')
        {
            return ManifestOperationResult<string>.Success(
                $"file://{ProjectPath.Normalize(trimmed).TrimEnd('/')}"
            );
        }

        var parsed = ParseLocation(trimmed);
        if (parsed.Value is not { } location)
        {
            return ManifestOperationResult<string>.Failure(
                parsed.Error ?? $"Repository URL '{url}' is not a supported Git URL."
            );
        }

        var embedsCredentials =
            location.UserInformation is { } credentials
            && (
                credentials.Contains(':', StringComparison.Ordinal)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            );
        if (embedsCredentials)
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

    private sealed record OciRegistryUrl(Uri Uri, string RawPath);

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

    [GeneratedRegex(
        @"^[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex OciRepositoryComponentPattern();
}
