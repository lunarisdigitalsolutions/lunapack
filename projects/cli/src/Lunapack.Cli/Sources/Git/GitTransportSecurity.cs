using System.Globalization;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Git;

internal sealed class GitTransportSecurity(
    Func<
        GitSecurityProbeRequest,
        CancellationToken,
        CancellationToken,
        Task<ManifestOperationResult<GitProcessOutput>>
    > probe
)
{
    internal static IReadOnlyList<string> CreateTransportArguments(
        GitTransportMode transportMode
    ) =>
        transportMode switch
        {
            GitTransportMode.SecureOnly =>
            [
                "-c",
                "protocol.allow=never",
                "-c",
                "protocol.file.allow=always",
                "-c",
                "protocol.https.allow=always",
                "-c",
                "protocol.ssh.allow=always",
            ],
            GitTransportMode.AllowHttp =>
            [
                "-c",
                "protocol.allow=never",
                "-c",
                "protocol.http.allow=always",
                "-c",
                "credential.helper=",
            ],
            GitTransportMode.AllowGit =>
            [
                "-c",
                "protocol.allow=never",
                "-c",
                "protocol.git.allow=always",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(transportMode), transportMode, null),
        };

    internal static void ConfigureTransportEnvironment(
        IDictionary<string, string?> environment,
        GitTransportMode transportMode,
        string? repositoryUrl = null
    )
    {
        environment["GIT_ALLOW_PROTOCOL"] = transportMode switch
        {
            GitTransportMode.SecureOnly => SourceIdentityNormalizer.IsLocalRepositoryPath(
                repositoryUrl
            )
                ? "file"
                : "https:ssh",
            GitTransportMode.AllowHttp => "http",
            GitTransportMode.AllowGit => "git",
            _ => throw new ArgumentOutOfRangeException(nameof(transportMode), transportMode, null),
        };
        environment.Remove("GIT_SSL_NO_VERIFY");
        environment.Remove("GIT_ALTERNATE_OBJECT_DIRECTORIES");
        environment.Remove("GIT_COMMON_DIR");
        environment.Remove("GIT_DIR");
        environment.Remove("GIT_INDEX_FILE");
        environment.Remove("GIT_OBJECT_DIRECTORY");
        environment.Remove("GIT_PREFIX");
        environment.Remove("GIT_WORK_TREE");
        environment["GIT_LFS_SKIP_SMUDGE"] = "1";
    }

    internal static string? ConfigureSecureHttps(
        IDictionary<string, string?> environment,
        GitTransportMode transportMode,
        string? repositoryUrl
    )
    {
        if (transportMode != GitTransportMode.SecureOnly || string.IsNullOrEmpty(repositoryUrl))
        {
            return null;
        }

        if (
            string.IsNullOrEmpty(repositoryUrl)
            || !Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(uri.Host)
        )
        {
            return repositoryUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? "Git repository URL is not a valid HTTPS URL."
                : null;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return "Git repository URLs with HTTPS queries or fragments are not supported.";
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "Git repository URLs must not embed HTTPS credentials.";
        }
        if (repositoryUrl.Contains('\\'))
        {
            return "Git HTTPS repository URLs must not contain backslashes.";
        }

        var configUrl = GetHttpsConfigurationUrl(repositoryUrl);
        if (configUrl is null)
        {
            return "Unable to enforce secure Git HTTPS settings for the repository URL.";
        }

        var configCount = 0;
        if (
            environment.TryGetValue("GIT_CONFIG_COUNT", out var configuredCount)
            && !int.TryParse(
                configuredCount,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out configCount
            )
        )
        {
            return "Unable to enforce secure Git HTTPS settings because GIT_CONFIG_COUNT is invalid.";
        }

        if (configCount < 0 || configCount > int.MaxValue - 2)
        {
            return "Unable to enforce secure Git HTTPS settings because GIT_CONFIG_COUNT is out of range.";
        }

        environment[$"GIT_CONFIG_KEY_{configCount}"] = $"http.{configUrl}.followRedirects";
        environment[$"GIT_CONFIG_VALUE_{configCount}"] = "false";
        environment[$"GIT_CONFIG_KEY_{configCount + 1}"] = $"http.{configUrl}.sslVerify";
        environment[$"GIT_CONFIG_VALUE_{configCount + 1}"] = "true";
        environment["GIT_CONFIG_COUNT"] = (configCount + 2).ToString(CultureInfo.InvariantCulture);
        return null;
    }

    internal async Task<ManifestOperationResult<string>> ResolveEffectiveRepositoryUrlAsync(
        string repositoryUrl,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string? workingDirectory,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation
    )
    {
        var result = await probe(
            new GitSecurityProbeRequest(
                workingDirectory is null
                    ? ["ls-remote", "--get-url", "--", repositoryUrl]
                    : ["-C", workingDirectory, "ls-remote", "--get-url", "--", repositoryUrl],
                timeout,
                transportMode,
                repositoryUrl
            ),
            operationCancellation,
            callerCancellation
        );
        if (result.Value is not { } output)
        {
            return ManifestOperationResult<string>.Failure(
                result.Error ?? "Unable to resolve the effective Git repository URL."
            );
        }
        if (output.StandardOutputTruncated || output.StandardOutput.Contains('\uFFFD'))
        {
            return ManifestOperationResult<string>.Failure(
                "Git returned an incomplete effective repository URL."
            );
        }

        var effectiveUrl = output.StandardOutput.EndsWith('\n')
            ? output.StandardOutput[..^1]
            : output.StandardOutput;
        if (
            SourceIdentityNormalizer.IsUncRepositoryPath(effectiveUrl)
            || !SourceIdentityNormalizer.IsLocalRepositoryPath(repositoryUrl)
                && SourceIdentityNormalizer.IsLocalRepositoryPath(effectiveUrl)
        )
        {
            return ManifestOperationResult<string>.Failure(
                "Git repository was rewritten to a disallowed local filesystem location."
            );
        }
        return
            effectiveUrl.Length == 0
            || effectiveUrl.Length > 64 * 1024
            || effectiveUrl.Contains('\n')
            || effectiveUrl.Contains('\r')
            || !string.Equals(effectiveUrl, effectiveUrl.Trim(), StringComparison.Ordinal)
            ? ManifestOperationResult<string>.Failure(
                "Git returned an invalid effective repository URL."
            )
            : ManifestOperationResult<string>.Success(effectiveUrl);
    }

    internal async Task<ManifestOperationResult<bool>> VerifySecureHttpsAsync(
        TimeSpan timeout,
        GitTransportMode transportMode,
        string? repositoryUrl,
        string? workingDirectory,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation
    )
    {
        var configUrl = GetHttpsConfigurationUrl(repositoryUrl);
        if (transportMode != GitTransportMode.SecureOnly || configUrl is null)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        var expectedSettings = new[]
        {
            ("http.followRedirects", "false"),
            ("http.sslVerify", "true"),
        };
        foreach (var (setting, expectedValue) in expectedSettings)
        {
            var result = await probe(
                new GitSecurityProbeRequest(
                    workingDirectory is null
                        ? ["config", "--get-urlmatch", setting, configUrl]
                        : ["-C", workingDirectory, "config", "--get-urlmatch", setting, configUrl],
                    timeout,
                    transportMode,
                    configUrl
                ),
                operationCancellation,
                callerCancellation
            );
            var output = result.Value;
            if (
                output is null
                || output.StandardOutputTruncated
                || output.StandardErrorTruncated
                || output.StandardOutput.Contains('\uFFFD')
                || !string.Equals(
                    ParseSingleLine(output.StandardOutput),
                    expectedValue,
                    StringComparison.Ordinal
                )
            )
            {
                return ManifestOperationResult<bool>.Failure(
                    "Unable to enforce secure Git HTTPS settings because inherited Git configuration overrides them."
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    internal static string? GetGitWorkingDirectory(IReadOnlyList<string> arguments)
    {
        var index = IndexOfArgument(arguments, "-C");
        return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
    }

    private static string? GetHttpsConfigurationUrl(string? repositoryUrl)
    {
        if (
            string.IsNullOrEmpty(repositoryUrl)
            || !Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || repositoryUrl.Contains('\\')
        )
        {
            return null;
        }

        return repositoryUrl;
    }

    private static int IndexOfArgument(IReadOnlyList<string> arguments, string expected)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], expected, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string? ParseSingleLine(string value)
    {
        var line =
            value.EndsWith("\r\n", StringComparison.Ordinal) ? value[..^2]
            : value.EndsWith('\n') ? value[..^1]
            : value;
        return line.Contains('\n') || line.Contains('\r') ? null : line;
    }
}
