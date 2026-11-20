using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Git;

internal sealed class GitProcessRunner(
    string executable = "git",
    long maximumStandardOutputBytes = 64L * 1024 * 1024
) : IGitProcessRunner
{
    private const int MaximumDiagnosticCharacters = 64 * 1024;

    public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStandardOutputBytes);
        return RunCoreAsync(
            arguments,
            timeout,
            maximumStandardOutputBytes,
            "Git standard output",
            cancellationToken
        );
    }

    public Task<ManifestOperationResult<GitProcessOutput>> RunBoundedAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        long standardOutputByteLimit,
        string outputName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(standardOutputByteLimit);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);

        return RunCoreAsync(
            arguments,
            timeout,
            standardOutputByteLimit,
            outputName,
            cancellationToken
        );
    }

    public async Task<ManifestOperationResult<GitProcessOutput>> RunBoundedRecordsAsync(
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
        if (timeout <= TimeSpan.Zero)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                "Git operation timeout must be greater than zero."
            );
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure("Git operation was canceled.");
        }

        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(arguments) };
            if (!process.Start())
            {
                return ManifestOperationResult<GitProcessOutput>.Failure(
                    "Unable to start the Git executable."
                );
            }

            return await CaptureBoundedRecordsAsync(
                process,
                timeout,
                recordLimit,
                outputName,
                recordSeparator,
                outputByteLimit,
                recordByteLimit,
                cancellationToken
            );
        }
        catch (Win32Exception exception)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"Unable to run Git executable '{executable}': {exception.Message}"
            );
        }
    }

    private static async Task<ManifestOperationResult<GitProcessOutput>> CaptureBoundedRecordsAsync(
        Process process,
        TimeSpan timeout,
        long recordLimit,
        string outputName,
        byte recordSeparator,
        long? outputByteLimit,
        long? recordByteLimit,
        CancellationToken cancellationToken
    )
    {
        using var limitCancellation = new CancellationTokenSource();
        var standardOutput = ReadBoundedRecordsAsync(
            process.StandardOutput.BaseStream,
            recordLimit,
            recordSeparator,
            outputByteLimit,
            recordByteLimit,
            limitCancellation.Cancel
        );
        var standardError = ReadBoundedAsync(process.StandardError);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token,
            limitCancellation.Token
        );
        try
        {
            await process.WaitForExitAsync(operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(process);
            await Task.WhenAll(standardOutput, standardError);
            return standardOutput.Result.ExceededLimit
                ? CreateBoundedRecordFailure(
                    outputName,
                    recordLimit,
                    outputByteLimit,
                    recordByteLimit,
                    standardOutput.Result
                )
                : ManifestOperationResult<GitProcessOutput>.Failure(
                    cancellationToken.IsCancellationRequested
                        ? "Git operation was canceled."
                        : $"Git operation timed out after {timeout.TotalSeconds:0} seconds."
                );
        }

        var captured = await standardOutput;
        if (captured.ExceededLimit)
        {
            return CreateBoundedRecordFailure(
                outputName,
                recordLimit,
                outputByteLimit,
                recordByteLimit,
                captured
            );
        }

        var output = new GitProcessOutput(captured.Text, (await standardError).Text);
        return process.ExitCode == 0
            ? ManifestOperationResult<GitProcessOutput>.Success(output)
            : ManifestOperationResult<GitProcessOutput>.Failure(
                CreateExitCodeError(process.ExitCode, output)
            );
    }

    private async Task<ManifestOperationResult<GitProcessOutput>> RunCoreAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        long? standardOutputByteLimit,
        string? outputName,
        CancellationToken cancellationToken
    )
    {
        if (timeout <= TimeSpan.Zero)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                "Git operation timeout must be greater than zero."
            );
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure("Git operation was canceled.");
        }

        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(arguments) };
            if (!process.Start())
            {
                return ManifestOperationResult<GitProcessOutput>.Failure(
                    "Unable to start the Git executable."
                );
            }

            return await CaptureProcessOutputAsync(
                process,
                timeout,
                standardOutputByteLimit,
                outputName,
                cancellationToken
            );
        }
        catch (Win32Exception exception)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"Unable to run Git executable '{executable}': {exception.Message}"
            );
        }
    }

    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<ManifestOperationResult<GitProcessOutput>> CaptureProcessOutputAsync(
        Process process,
        TimeSpan timeout,
        long? standardOutputByteLimit,
        string? outputName,
        CancellationToken cancellationToken
    )
    {
        using var outputLimitCancellation = new CancellationTokenSource();
        var standardOutput = standardOutputByteLimit is { } byteLimit
            ? ReadBoundedBytesAsync(
                process.StandardOutput.BaseStream,
                byteLimit,
                outputLimitCancellation.Cancel
            )
            : ReadBoundedAsync(process.StandardOutput);
        var standardError = ReadBoundedAsync(process.StandardError);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token,
            outputLimitCancellation.Token
        );

        try
        {
            await process.WaitForExitAsync(operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(process);
            await Task.WhenAll(standardOutput, standardError);

            if (standardOutput.Result.ExceededLimit)
            {
                return CreateOutputLimitFailure(
                    outputName,
                    standardOutputByteLimit,
                    standardOutput.Result.ObservedBytes
                );
            }

            return ManifestOperationResult<GitProcessOutput>.Failure(
                cancellationToken.IsCancellationRequested
                    ? "Git operation was canceled."
                    : $"Git operation timed out after {timeout.TotalSeconds:0} seconds."
            );
        }

        var capturedOutput = await standardOutput;
        if (capturedOutput.ExceededLimit)
        {
            return CreateOutputLimitFailure(
                outputName,
                standardOutputByteLimit,
                capturedOutput.ObservedBytes
            );
        }

        var output = new GitProcessOutput(capturedOutput.Text, (await standardError).Text);
        return process.ExitCode == 0
            ? ManifestOperationResult<GitProcessOutput>.Success(output)
            : ManifestOperationResult<GitProcessOutput>.Failure(
                CreateExitCodeError(process.ExitCode, output)
            );
    }

    internal static string CreateExitCodeError(int exitCode, GitProcessOutput output)
    {
        var diagnostic = $"{output.StandardError}\n{output.StandardOutput}";
        var guidance =
            ContainsAny(
                diagnostic,
                "authentication failed",
                "could not read username",
                "could not read password",
                "terminal prompts disabled",
                "no credentials available"
            )
                ? "Git authentication failed. Configure the operating-system Git credential helper or SSH credentials for this source."
            : ContainsAny(
                diagnostic,
                "permission denied",
                "access denied",
                "not authorized",
                "repository not found",
                "does not appear to be a git repository"
            )
                ? "Git access was denied or the repository was not found. Verify the repository URL, permissions, and configured Git credentials or SSH identity."
            : ContainsAny(
                diagnostic,
                "certificate verify failed",
                "ssl certificate problem",
                "tls certificate",
                "schannel"
            )
                ? "Git TLS certificate verification failed. Fix the certificate trust configuration; do not disable certificate checks."
            : ContainsAny(
                diagnostic,
                "could not resolve host",
                "temporary failure in name resolution",
                "failed to connect",
                "connection refused",
                "network is unreachable",
                "no route to host",
                "connection timed out",
                "operation timed out"
            )
                ? "Git host is unavailable. Check network and DNS access, then retry."
            : "Check the Git source URL, repository access, and host availability.";

        return $"Git exited with code {exitCode}: {guidance}";
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static ManifestOperationResult<GitProcessOutput> CreateOutputLimitFailure(
        string? outputName,
        long? byteLimit,
        long observedBytes
    ) =>
        ManifestOperationResult<GitProcessOutput>.Failure(
            $"{outputName ?? "Git output"} exceeds its {byteLimit} byte limit (at least {observedBytes} bytes read).",
            ManifestOperationErrorKind.ResourceLimitExceeded
        );

    private static ManifestOperationResult<GitProcessOutput> CreateRecordLimitFailure(
        string outputName,
        long recordLimit,
        long observedRecords
    ) =>
        ManifestOperationResult<GitProcessOutput>.Failure(
            $"{outputName} exceeds its {recordLimit} record limit (at least {observedRecords} records read).",
            ManifestOperationErrorKind.ResourceLimitExceeded
        );

    private static ManifestOperationResult<GitProcessOutput> CreateBoundedRecordFailure(
        string outputName,
        long recordLimit,
        long? outputByteLimit,
        long? recordByteLimit,
        BoundedOutput output
    ) =>
        output.Limit switch
        {
            BoundedOutputLimit.Records => CreateRecordLimitFailure(
                outputName,
                recordLimit,
                output.ObservedRecords
            ),
            BoundedOutputLimit.OutputBytes => ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} exceeds its {outputByteLimit} byte limit (at least {output.ObservedBytes} bytes read).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            ),
            BoundedOutputLimit.RecordBytes => ManifestOperationResult<GitProcessOutput>.Failure(
                $"{outputName} record exceeds its {recordByteLimit} byte limit (at least {output.ObservedRecordBytes} bytes read).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            ),
            _ => CreateRecordLimitFailure(outputName, recordLimit, output.ObservedRecords),
        };

    private static async Task<BoundedOutput> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var captured = new StringBuilder(MaximumDiagnosticCharacters);
        while (true)
        {
            var charactersRead = await reader.ReadAsync(buffer.AsMemory());
            if (charactersRead == 0)
            {
                return new BoundedOutput(captured.ToString(), 0, 0, false);
            }

            var remainingCapacity = MaximumDiagnosticCharacters - captured.Length;
            if (remainingCapacity > 0)
            {
                captured.Append(buffer, 0, Math.Min(charactersRead, remainingCapacity));
            }
        }
    }

    private static async Task<BoundedOutput> ReadBoundedBytesAsync(
        Stream stream,
        long byteLimit,
        Action stopProcess
    )
    {
        var buffer = new byte[81_920];
        await using var captured = new MemoryStream();
        long bytesRead = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                return new BoundedOutput(
                    Encoding.UTF8.GetString(captured.GetBuffer(), 0, checked((int)captured.Length)),
                    bytesRead,
                    0,
                    false
                );
            }

            var observedBytes = checked(bytesRead + read);
            if (observedBytes > byteLimit)
            {
                stopProcess();
                return new BoundedOutput(
                    string.Empty,
                    observedBytes,
                    0,
                    true,
                    BoundedOutputLimit.OutputBytes
                );
            }

            await captured.WriteAsync(buffer.AsMemory(0, read));
            bytesRead = observedBytes;
        }
    }

    private static async Task<BoundedOutput> ReadBoundedRecordsAsync(
        Stream stream,
        long recordLimit,
        byte separator,
        long? outputByteLimit,
        long? recordByteLimit,
        Action stopProcess
    )
    {
        var buffer = new byte[81_920];
        await using var captured = new MemoryStream();
        var state = new BoundedRecordState(
            captured,
            recordLimit,
            separator,
            outputByteLimit,
            recordByteLimit
        );
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                return state.Complete();
            }

            var failure = state.Write(buffer.AsSpan(0, read));
            if (failure is not null)
            {
                stopProcess();
                return failure;
            }
        }
    }

    private sealed class BoundedRecordState(
        MemoryStream captured,
        long recordLimit,
        byte separator,
        long? outputByteLimit,
        long? recordByteLimit
    )
    {
        private bool _inRecord;
        private long _outputBytesRead;
        private long _recordBytesRead;
        private long _recordsRead;

        public BoundedOutput Complete()
        {
            if (_inRecord)
            {
                _recordsRead++;
            }

            return _recordsRead > recordLimit
                ? CreateFailure(BoundedOutputLimit.Records, captured.Length)
                : new BoundedOutput(
                    Encoding.UTF8.GetString(captured.GetBuffer(), 0, checked((int)captured.Length)),
                    captured.Length,
                    _recordsRead,
                    false
                );
        }

        public BoundedOutput? Write(ReadOnlySpan<byte> bytes)
        {
            foreach (var value in bytes)
            {
                var observedOutputBytes = checked(_outputBytesRead + 1);
                if (observedOutputBytes > outputByteLimit)
                {
                    return CreateFailure(
                        BoundedOutputLimit.OutputBytes,
                        observedOutputBytes,
                        _recordBytesRead
                    );
                }

                if (value == separator)
                {
                    if (_inRecord)
                    {
                        _recordsRead++;
                        _inRecord = false;
                    }

                    captured.WriteByte(value);
                    _outputBytesRead = observedOutputBytes;
                    _recordBytesRead = 0;
                    continue;
                }

                if (!_inRecord)
                {
                    if (_recordsRead >= recordLimit)
                    {
                        return CreateFailure(BoundedOutputLimit.Records, captured.Length);
                    }

                    _inRecord = true;
                }

                var observedRecordBytes = checked(_recordBytesRead + 1);
                if (observedRecordBytes > recordByteLimit)
                {
                    return CreateFailure(
                        BoundedOutputLimit.RecordBytes,
                        observedOutputBytes,
                        observedRecordBytes
                    );
                }

                captured.WriteByte(value);
                _outputBytesRead = observedOutputBytes;
                _recordBytesRead = observedRecordBytes;
            }

            return null;
        }

        private BoundedOutput CreateFailure(
            BoundedOutputLimit limit,
            long observedBytes,
            long observedRecordBytes = 0
        ) =>
            new(
                string.Empty,
                observedBytes,
                limit == BoundedOutputLimit.Records ? _recordsRead + 1 : _recordsRead,
                true,
                limit,
                observedRecordBytes
            );
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // The process exited while cleanup raced with cancellation.
        }
    }

    private sealed record BoundedOutput(
        string Text,
        long ObservedBytes,
        long ObservedRecords,
        bool ExceededLimit,
        BoundedOutputLimit Limit = BoundedOutputLimit.None,
        long ObservedRecordBytes = 0
    );

    private enum BoundedOutputLimit
    {
        None,
        Bytes,
        Records,
        OutputBytes,
        RecordBytes,
    }
}
