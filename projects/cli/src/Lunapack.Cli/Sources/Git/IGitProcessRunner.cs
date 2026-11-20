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

    async Task<ManifestOperationResult<GitProcessOutput>> RunBoundedAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        long standardOutputByteLimit,
        string outputName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(standardOutputByteLimit);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);

        var result = await RunAsync(arguments, timeout, cancellationToken);
        if (result.Value is not { } output)
        {
            return result;
        }

        var byteCount = Encoding.UTF8.GetByteCount(output.StandardOutput);
        return byteCount <= standardOutputByteLimit
            ? result
            : ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} exceeds its {standardOutputByteLimit} byte limit (at least {byteCount} bytes read).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordLimit);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);
        if (outputByteLimit is <= 0 || recordByteLimit is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                outputByteLimit is <= 0 ? nameof(outputByteLimit) : nameof(recordByteLimit)
            );
        }

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
        if (outputByteLimit is { } byteLimit && outputByteCount > byteLimit)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} exceeds its {byteLimit} byte limit (at least {outputByteCount} bytes read).",
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
