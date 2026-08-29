using System.Text.RegularExpressions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Sources.Git;

internal sealed partial class GitRefResolver(IGitProcessRunner processRunner)
{
    private const int DefaultTimeoutSeconds = 300;

    public async Task<ManifestOperationResult<GitSourceResolution>> ResolveAsync(
        ProjectConfiguration.GitSource source,
        string? cachedDefaultBranch,
        CancellationToken cancellationToken
    )
    {
        var timeout = TimeSpan.FromSeconds(source.TimeoutSeconds ?? DefaultTimeoutSeconds);
        if (source.Ref is { } configuredRef)
        {
            return await ResolveExplicitRefAsync(
                source.Url,
                configuredRef,
                timeout,
                cancellationToken
            );
        }

        if (cachedDefaultBranch is { } defaultBranch)
        {
            var cachedResolution = await ResolveExplicitRefAsync(
                source.Url,
                defaultBranch,
                timeout,
                cancellationToken
            );
            if (cachedResolution.IsSuccess)
            {
                return cachedResolution.Value is { } resolution
                    ? ManifestOperationResult<GitSourceResolution>.Success(
                        resolution with
                        {
                            DefaultBranch = defaultBranch,
                        }
                    )
                    : ManifestOperationResult<GitSourceResolution>.Failure(
                        "Git returned no resolution for the cached default branch."
                    );
            }
        }

        return await ResolveRemoteHeadAsync(source.Url, timeout, cancellationToken);
    }

    public async Task<ManifestOperationResult<GitCanonicalRef>> ResolveCanonicalRefAsync(
        string repositoryUrl,
        string reference,
        TimeSpan? timeout,
        CancellationToken cancellationToken
    )
    {
        var trimmedReference = reference?.Trim() ?? string.Empty;
        if (trimmedReference.Length == 0)
        {
            return ManifestOperationResult<GitCanonicalRef>.Failure(
                "A Git ref is required to canonicalize an external source."
            );
        }

        if (CommitPattern().IsMatch(trimmedReference))
        {
            var commit = trimmedReference.ToLowerInvariant();
            return ManifestOperationResult<GitCanonicalRef>.Success(
                new GitCanonicalRef(commit, commit)
            );
        }

        var command = await processRunner.RunAsync(
            ["ls-remote", "--exit-code", repositoryUrl, trimmedReference],
            timeout ?? TimeSpan.FromSeconds(DefaultTimeoutSeconds),
            cancellationToken
        );
        if (command.Value is not { } output)
        {
            return ManifestOperationResult<GitCanonicalRef>.Failure(
                command.Error ?? $"Unable to resolve Git ref '{reference}'."
            );
        }

        var candidates = ParseCanonicalRefs(output.StandardOutput);
        if (candidates.Count == 0)
        {
            return ManifestOperationResult<GitCanonicalRef>.Failure(
                $"Git ref '{reference}' did not resolve to an immutable commit."
            );
        }

        if (candidates.Count > 1)
        {
            return ManifestOperationResult<GitCanonicalRef>.Failure(
                $"Git ref '{reference}' is ambiguous and matches {string.Join(", ", candidates.Keys.Order(StringComparer.Ordinal))}."
            );
        }

        var (canonicalRef, resolvedCommit) = candidates.First();
        return ManifestOperationResult<GitCanonicalRef>.Success(
            new GitCanonicalRef(canonicalRef, resolvedCommit)
        );
    }

    internal static Dictionary<string, string> ParseCanonicalRefs(string output)
    {
        var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2 || !CommitPattern().IsMatch(fields[0]))
            {
                continue;
            }

            var isPeeled = fields[1].EndsWith("^{}", StringComparison.Ordinal);
            var refName = isPeeled ? fields[1][..^3] : fields[1];
            if (isPeeled || !candidates.ContainsKey(refName))
            {
                candidates[refName] = fields[0].ToLowerInvariant();
            }
        }

        return candidates;
    }

    private async Task<ManifestOperationResult<GitSourceResolution>> ResolveExplicitRefAsync(
        string repositoryUrl,
        string reference,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        if (CommitPattern().IsMatch(reference))
        {
            return ManifestOperationResult<GitSourceResolution>.Success(
                new GitSourceResolution(reference.ToLowerInvariant(), null)
            );
        }

        var command = await processRunner.RunAsync(
            ["ls-remote", "--exit-code", repositoryUrl, reference],
            timeout,
            cancellationToken
        );
        if (command.Value is not { } output)
        {
            return ManifestOperationResult<GitSourceResolution>.Failure(
                command.Error ?? $"Unable to resolve Git ref '{reference}'."
            );
        }

        var resolvedCommit = ParseResolvedCommit(output.StandardOutput);
        return resolvedCommit is null
            ? ManifestOperationResult<GitSourceResolution>.Failure(
                $"Git ref '{reference}' did not resolve to an immutable commit."
            )
            : ManifestOperationResult<GitSourceResolution>.Success(
                new GitSourceResolution(resolvedCommit, null)
            );
    }

    private async Task<ManifestOperationResult<GitSourceResolution>> ResolveRemoteHeadAsync(
        string repositoryUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var command = await processRunner.RunAsync(
            ["ls-remote", "--symref", repositoryUrl, "HEAD"],
            timeout,
            cancellationToken
        );
        if (command.Value is not { } output)
        {
            return ManifestOperationResult<GitSourceResolution>.Failure(
                command.Error ?? "Unable to resolve the remote Git HEAD."
            );
        }

        var defaultBranch = ParseDefaultBranch(output.StandardOutput);
        var resolvedCommit = ParseResolvedCommit(output.StandardOutput);
        return defaultBranch is null || resolvedCommit is null
            ? ManifestOperationResult<GitSourceResolution>.Failure(
                "Remote Git HEAD did not identify a default branch and immutable commit."
            )
            : ManifestOperationResult<GitSourceResolution>.Success(
                new GitSourceResolution(resolvedCommit, defaultBranch)
            );
    }

    internal static string? ParseDefaultBranch(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmedLine = line.Trim();
            if (!trimmedLine.StartsWith("ref: refs/heads/", StringComparison.Ordinal))
            {
                continue;
            }

            var headSeparator = trimmedLine.IndexOf('\t');
            if (headSeparator < 0 || !trimmedLine.EndsWith("\tHEAD", StringComparison.Ordinal))
            {
                continue;
            }

            var branch = trimmedLine["ref: refs/heads/".Length..headSeparator];
            return branch.Length == 0 ? null : branch;
        }

        return null;
    }

    internal static string? ParseResolvedCommit(string output)
    {
        string? resolvedCommit = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2 || !CommitPattern().IsMatch(fields[0]))
            {
                continue;
            }

            if (fields[1].EndsWith("^{}", StringComparison.Ordinal))
            {
                return fields[0].ToLowerInvariant();
            }

            resolvedCommit ??= fields[0].ToLowerInvariant();
        }

        return resolvedCommit;
    }

    [GeneratedRegex("^[A-Fa-f0-9]{40}(?:[A-Fa-f0-9]{24})?$", RegexOptions.None, 1000)]
    private static partial Regex CommitPattern();
}
