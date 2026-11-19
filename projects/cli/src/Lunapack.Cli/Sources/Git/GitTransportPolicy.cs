using System.Collections.Concurrent;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Sources.Git;

internal sealed class GitTransportPolicy(CliPreferenceService preferenceService, CliConsole console)
{
    private readonly ConcurrentDictionary<string, byte> _warnedSources = new(
        StringComparer.Ordinal
    );

    public async Task<ManifestOperationResult<GitTransportMode>> AuthorizeAsync(
        string projectDirectory,
        string repositoryUrl,
        string? sourceName = null
    )
    {
        var classification = ClassifyRepository(repositoryUrl);
        if (classification.Value is not { } classified)
        {
            return ManifestOperationResult<GitTransportMode>.Failure(
                classification.Error ?? "Unable to classify the Git repository transport."
            );
        }
        if (classified.Mode == GitTransportMode.SecureOnly)
        {
            return ManifestOperationResult<GitTransportMode>.Success(classified.Mode);
        }
        if (classified.Uri is not { } uri)
        {
            return ManifestOperationResult<GitTransportMode>.Failure(
                "Unable to identify the insecure Git repository URL."
            );
        }

        var preferences = await preferenceService.ResolveAsync(projectDirectory);
        if (preferences.Value is not { } effectivePreferences)
        {
            return ManifestOperationResult<GitTransportMode>.Failure(
                preferences.Error ?? "Unable to resolve Git transport preferences."
            );
        }

        var protocol = classified.Mode == GitTransportMode.AllowHttp ? "http" : "git";
        var sourceLabel = GetSourceLabel(sourceName, uri);
        if (!effectivePreferences.AllowInsecureGitTransport)
        {
            return ManifestOperationResult<GitTransportMode>.Failure(
                $"Git source '{sourceLabel}' uses insecure {protocol} transport. Set 'git.allow-insecure-transport' to true in the project, user-project, or global scope, or use HTTPS or SSH."
            );
        }

        var identity = SourceIdentityNormalizer.CreateGit(classified.RepositoryUrl, null, null);
        var warningKey = identity.Value?.Value ?? $"{uri.Host}:{uri.Port}{uri.AbsolutePath}";
        if (_warnedSources.TryAdd(warningKey, 0))
        {
            console.Warning(
                $"Insecure Git transport '{protocol}' is enabled for source '{sourceLabel}'. Network traffic can be read or modified; prefer HTTPS or SSH."
            );
        }

        return ManifestOperationResult<GitTransportMode>.Success(classified.Mode);
    }

    private static ManifestOperationResult<GitTransportClassification> ClassifyRepository(
        string repositoryUrl
    )
    {
        var trimmedUrl = repositoryUrl?.Trim() ?? string.Empty;
        if (SourceIdentityNormalizer.IsUncRepositoryPath(trimmedUrl))
        {
            return ManifestOperationResult<GitTransportClassification>.Failure(
                "Git repository locations must not use UNC or file URL locations."
            );
        }
        if (IsRemoteHelperRepository(trimmedUrl))
        {
            return ManifestOperationResult<GitTransportClassification>.Failure(
                "Git remote-helper repository locations are not supported."
            );
        }
        if (!string.Equals(repositoryUrl, trimmedUrl, StringComparison.Ordinal))
        {
            return ManifestOperationResult<GitTransportClassification>.Failure(
                "Git repository URL must not contain leading or trailing whitespace."
            );
        }
        if (trimmedUrl.StartsWith('-'))
        {
            return ManifestOperationResult<GitTransportClassification>.Failure(
                "Git repository locations must not begin with '-'."
            );
        }
        if (
            IsScpStyleRepository(trimmedUrl)
            || !Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri)
        )
        {
            return ManifestOperationResult<GitTransportClassification>.Success(
                new GitTransportClassification(trimmedUrl, GitTransportMode.SecureOnly, null)
            );
        }
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return ManifestOperationResult<GitTransportClassification>.Failure(
                "Git repository URLs with queries or fragments are not supported."
            );
        }
        if (
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && trimmedUrl.Contains('\\')
        )
        {
            return ManifestOperationResult<GitTransportClassification>.Failure(
                "Git HTTPS repository URLs must not contain backslashes."
            );
        }

        var mode = uri.Scheme.ToLowerInvariant() switch
        {
            "http" => GitTransportMode.AllowHttp,
            "git" => GitTransportMode.AllowGit,
            _ => GitTransportMode.SecureOnly,
        };
        return ManifestOperationResult<GitTransportClassification>.Success(
            new GitTransportClassification(trimmedUrl, mode, uri)
        );
    }

    private static bool IsScpStyleRepository(string repositoryUrl)
    {
        var separator = repositoryUrl.IndexOf(':');
        if (
            separator <= 0
            || separator == repositoryUrl.Length - 1
            || repositoryUrl.Contains("://", StringComparison.Ordinal)
            || repositoryUrl[..separator].IndexOfAny(['/', '\\']) >= 0
        )
        {
            return false;
        }

        return separator != 1
            || !char.IsAsciiLetter(repositoryUrl[0])
            || !repositoryUrl.AsSpan(2).StartsWith(['/', '\\']);
    }

    private static bool IsRemoteHelperRepository(string repositoryUrl)
    {
        if (repositoryUrl.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        var separator = repositoryUrl.IndexOf(':');
        return separator >= 0
            && separator + 1 < repositoryUrl.Length
            && repositoryUrl[separator + 1] == ':';
    }

    private static string GetSourceLabel(string? sourceName, Uri uri)
    {
        if (
            !string.IsNullOrWhiteSpace(sourceName)
            && !sourceName.Contains('@')
            && !sourceName.Contains("://", StringComparison.Ordinal)
        )
        {
            return sourceName.Trim();
        }

        return string.IsNullOrEmpty(uri.Host) ? "repository" : uri.Host;
    }

    private sealed record GitTransportClassification(
        string RepositoryUrl,
        GitTransportMode Mode,
        Uri? Uri
    );
}
