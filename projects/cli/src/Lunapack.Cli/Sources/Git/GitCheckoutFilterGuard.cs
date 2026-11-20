using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Git;

internal sealed class GitCheckoutFilterGuard(
    Func<
        GitSecurityProbeRequest,
        CancellationToken,
        CancellationToken,
        Task<ManifestOperationResult<GitProcessOutput>>
    > probe
)
{
    private const int MaximumCheckoutPathCharacters = 4 * 1024 * 1024;

    internal async Task<ManifestOperationResult<bool>> VerifyAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string? repositoryUrl,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation
    )
    {
        var checkoutIndex = IndexOfArgument(arguments, "checkout");
        var workingDirectoryIndex = IndexOfArgument(arguments, "-C");
        if (
            repositoryUrl is null
            || checkoutIndex < 0
            || workingDirectoryIndex < 0
            || workingDirectoryIndex + 1 >= arguments.Count
            || workingDirectoryIndex > checkoutIndex
        )
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        var indexPath = Path.Combine(Path.GetTempPath(), $"git-index-{Guid.NewGuid():N}");
        try
        {
            return await VerifyUsingIndexAsync(
                arguments[workingDirectoryIndex + 1],
                indexPath,
                timeout,
                transportMode,
                repositoryUrl,
                operationCancellation,
                callerCancellation
            );
        }
        finally
        {
            DeleteTemporaryIndex(indexPath);
        }
    }

    private async Task<ManifestOperationResult<bool>> VerifyUsingIndexAsync(
        string workingDirectory,
        string indexPath,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string repositoryUrl,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation
    )
    {
        var sentinelDrivers = await VerifyReservedFilterDriversAsync(
            workingDirectory,
            timeout,
            transportMode,
            repositoryUrl,
            operationCancellation,
            callerCancellation
        );
        if (!sentinelDrivers.IsSuccess)
        {
            return sentinelDrivers;
        }

        var listed = await ListCheckoutPathsAsync(
            workingDirectory,
            indexPath,
            timeout,
            transportMode,
            repositoryUrl,
            operationCancellation,
            callerCancellation
        );
        if (listed.Value is not { } paths)
        {
            return ManifestOperationResult<bool>.Failure(
                listed.Error ?? "Unable to inspect Git paths before checkout."
            );
        }

        var pendingPaths = new List<string>();
        var pendingCharacters = 0;
        foreach (var path in paths)
        {
            if (pendingPaths.Count > 0 && pendingCharacters + path.Length + 1 > 16 * 1024)
            {
                var batch = await VerifyBatchAsync(
                    workingDirectory,
                    pendingPaths,
                    timeout,
                    transportMode,
                    repositoryUrl,
                    indexPath,
                    operationCancellation,
                    callerCancellation
                );
                if (!batch.IsSuccess)
                {
                    return batch;
                }
                pendingPaths.Clear();
                pendingCharacters = 0;
            }

            pendingPaths.Add(path);
            pendingCharacters += path.Length + 1;
        }

        return pendingPaths.Count == 0
            ? ManifestOperationResult<bool>.Success(true)
            : await VerifyBatchAsync(
                workingDirectory,
                pendingPaths,
                timeout,
                transportMode,
                repositoryUrl,
                indexPath,
                operationCancellation,
                callerCancellation
            );
    }

    private async Task<ManifestOperationResult<IReadOnlyList<string>>> ListCheckoutPathsAsync(
        string workingDirectory,
        string indexPath,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string repositoryUrl,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation
    )
    {
        var indexed = await probe(
            new GitSecurityProbeRequest(
                ["-C", workingDirectory, "read-tree", "FETCH_HEAD"],
                timeout,
                transportMode,
                repositoryUrl,
                GitIndexPath: indexPath
            ),
            operationCancellation,
            callerCancellation
        );
        if (!indexed.IsSuccess)
        {
            return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                indexed.Error ?? "Unable to prepare Git attributes before checkout."
            );
        }

        var listed = await probe(
            new GitSecurityProbeRequest(
                ["-C", workingDirectory, "ls-tree", "-r", "-z", "--name-only", "FETCH_HEAD"],
                timeout,
                transportMode,
                repositoryUrl,
                MaximumOutputCharacters: MaximumCheckoutPathCharacters
            ),
            operationCancellation,
            callerCancellation
        );
        if (listed.Value is not { } paths)
        {
            return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                listed.Error ?? "Unable to inspect Git paths before checkout."
            );
        }
        if (paths.StandardOutputTruncated || paths.StandardOutput.Contains('\uFFFD'))
        {
            return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                "Git paths could not be inspected completely before checkout."
            );
        }

        return ManifestOperationResult<IReadOnlyList<string>>.Success(
            paths.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
        );
    }

    private async Task<ManifestOperationResult<bool>> VerifyBatchAsync(
        string workingDirectory,
        List<string> paths,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string repositoryUrl,
        string gitIndexPath,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation
    )
    {
        var result = await probe(
            new GitSecurityProbeRequest(
                ["-C", workingDirectory, "check-attr", "--cached", "-z", "--stdin", "filter"],
                timeout,
                transportMode,
                repositoryUrl,
                string.Join('\0', paths) + '\0',
                gitIndexPath
            ),
            operationCancellation,
            callerCancellation
        );
        if (result.Value is not { } output)
        {
            return ManifestOperationResult<bool>.Failure(
                result.Error ?? "Unable to inspect Git filter attributes before checkout."
            );
        }
        if (output.StandardOutputTruncated || output.StandardOutput.Contains('\uFFFD'))
        {
            return ManifestOperationResult<bool>.Failure(
                "Git filter attributes could not be inspected completely before checkout."
            );
        }

        var fields = output.StandardOutput.Split('\0');
        if ((fields.Length - 1) % 3 != 0 || (fields.Length - 1) / 3 != paths.Count)
        {
            return ManifestOperationResult<bool>.Failure(
                "Git returned malformed filter attributes before checkout."
            );
        }

        for (var index = 0; index < fields.Length - 1; index += 3)
        {
            if (
                !string.Equals(fields[index], paths[index / 3], StringComparison.Ordinal)
                || !string.Equals(fields[index + 1], "filter", StringComparison.Ordinal)
            )
            {
                return ManifestOperationResult<bool>.Failure(
                    "Git returned mismatched filter attributes before checkout."
                );
            }

            var value = fields[index + 2];
            if (
                !string.Equals(value, "unspecified", StringComparison.Ordinal)
                && !string.Equals(value, "unset", StringComparison.Ordinal)
            )
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Git checkout is blocked because '{fields[index]}' selects a smudge filter."
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private async Task<ManifestOperationResult<bool>> VerifyReservedFilterDriversAsync(
        string workingDirectory,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string repositoryUrl,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation
    )
    {
        var result = await probe(
            new GitSecurityProbeRequest(
                ["-C", workingDirectory, "config", "--null", "--list"],
                timeout,
                transportMode,
                repositoryUrl
            ),
            operationCancellation,
            callerCancellation
        );
        if (result.Value is not { } output)
        {
            return ManifestOperationResult<bool>.Failure(
                result.Error ?? "Unable to inspect Git filter configuration before checkout."
            );
        }
        if (output.StandardOutputTruncated || output.StandardOutput.Contains('\uFFFD'))
        {
            return ManifestOperationResult<bool>.Failure(
                "Git filter configuration could not be inspected completely before checkout."
            );
        }

        foreach (
            var entry in output.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var separator = entry.IndexOf('\n');
            var key = separator < 0 ? entry : entry[..separator];
            if (
                string.Equals(key, "filter.unset.smudge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "filter.unset.process", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    key,
                    "filter.unspecified.smudge",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    key,
                    "filter.unspecified.process",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return ManifestOperationResult<bool>.Failure(
                    "Git checkout is blocked because a configured filter driver uses a reserved attribute-state name."
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
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

    private static void DeleteTemporaryIndex(string indexPath)
    {
        foreach (var path in new[] { indexPath, $"{indexPath}.lock" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
