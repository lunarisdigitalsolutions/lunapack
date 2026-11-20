using System.Text;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Git;

internal interface IGitProcessRunner
{
    Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken
    );

    Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        GitTransportMode transportMode,
        CancellationToken cancellationToken
    ) => RunAsync(arguments, timeout, cancellationToken);

    Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string? repositoryUrl,
        CancellationToken cancellationToken
    ) => RunAsync(arguments, timeout, transportMode, cancellationToken);

    async Task<ManifestOperationResult<GitProcessOutput>> RunBoundedAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        long standardOutputByteLimit,
        string outputName,
        CancellationToken cancellationToken = default
    )
    {
        var result = await RunAsync(arguments, timeout, cancellationToken);
        if (result.Value is not { } output)
        {
            return result;
        }

        var byteCount = Encoding.UTF8.GetByteCount(output.StandardOutput);
        return output.StandardOutputTruncated || byteCount > standardOutputByteLimit
            ? ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} exceeds its {standardOutputByteLimit} byte limit (at least {byteCount} bytes read).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            )
            : result;
    }

    async Task<ManifestOperationResult<GitProcessOutput>> RunBoundedRecordsAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        long recordLimit,
        string outputName,
        byte recordSeparator,
        long? outputByteLimit = null,
        long? recordByteLimit = null,
        CancellationToken cancellationToken = default
    )
    {
        var result = await RunAsync(arguments, timeout, cancellationToken);
        if (result.Value is not { } output)
        {
            return result;
        }

        var records = output.StandardOutput.Split(
            (char)recordSeparator,
            StringSplitOptions.RemoveEmptyEntries
        );
        if (records.Length > recordLimit)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} exceeds its {recordLimit} record limit (at least {records.Length} records read).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var outputByteCount = Encoding.UTF8.GetByteCount(output.StandardOutput);
        if (
            output.StandardOutputTruncated
            || (outputByteLimit is { } byteLimit && outputByteCount > byteLimit)
        )
        {
            var effectiveLimit = outputByteLimit ?? outputByteCount;
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} exceeds its {effectiveLimit} byte limit (at least {outputByteCount} bytes read).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        if (
            recordByteLimit is { } singleRecordByteLimit
            && records.Any(record => Encoding.UTF8.GetByteCount(record) > singleRecordByteLimit)
        )
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} record exceeds its {singleRecordByteLimit} byte limit.",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        return result;
    }

    async Task<ManifestOperationResult<GitProcessOutput>> RunBoundedAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        long standardOutputByteLimit,
        string outputName,
        GitTransportMode transportMode,
        string? repositoryUrl,
        CancellationToken cancellationToken
    )
    {
        var result = await RunAsync(
            arguments,
            timeout,
            transportMode,
            repositoryUrl,
            cancellationToken
        );
        if (result.Value is not { } output)
        {
            return result;
        }

        var byteCount = Encoding.UTF8.GetByteCount(output.StandardOutput);
        return output.StandardOutputTruncated || byteCount > standardOutputByteLimit
            ? ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} exceeds its {standardOutputByteLimit} byte limit (at least {byteCount} bytes read).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            )
            : result;
    }

    async Task<ManifestOperationResult<GitProcessOutput>> RunBoundedRecordsAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        long recordLimit,
        string outputName,
        byte recordSeparator,
        long? outputByteLimit,
        long? recordByteLimit,
        GitTransportMode transportMode,
        string? repositoryUrl,
        CancellationToken cancellationToken
    )
    {
        var result = await RunAsync(
            arguments,
            timeout,
            transportMode,
            repositoryUrl,
            cancellationToken
        );
        if (result.Value is not { } output)
        {
            return result;
        }

        var records = output.StandardOutput.Split(
            (char)recordSeparator,
            StringSplitOptions.RemoveEmptyEntries
        );
        if (records.Length > recordLimit)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} exceeds its {recordLimit} record limit (at least {records.Length} records read).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var outputByteCount = Encoding.UTF8.GetByteCount(output.StandardOutput);
        if (
            output.StandardOutputTruncated
            || (outputByteLimit is { } byteLimit && outputByteCount > byteLimit)
        )
        {
            var effectiveLimit = outputByteLimit ?? outputByteCount;
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} exceeds its {effectiveLimit} byte limit (at least {outputByteCount} bytes read).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        if (
            recordByteLimit is { } singleRecordByteLimit
            && records.Any(record => Encoding.UTF8.GetByteCount(record) > singleRecordByteLimit)
        )
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} record exceeds its {singleRecordByteLimit} byte limit.",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        return result;
    }
}
