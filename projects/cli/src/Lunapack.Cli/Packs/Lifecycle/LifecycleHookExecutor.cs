using System.ComponentModel;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Security;
using System.Text;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed class LifecycleHookExecutor(
    IFileSystem fileSystem,
    CliConsole console,
    LifecycleHookHistoryStore? historyStore = null,
    TimeProvider? timeProvider = null
)
{
    private const int MaximumOutputCharacters = 64 * 1024;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ManifestOperationResult<bool>> ExecuteAsync(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        CancellationToken cancellationToken = default
    )
    {
        var integrity = VerifyPackedFile(invocation);
        if (!integrity.IsSuccess)
        {
            return integrity;
        }

        var timestamp = _timeProvider.GetUtcNow();
        var startedAt = Stopwatch.GetTimestamp();
        return await ExecuteProcessAsync(
            projectDirectory,
            invocation,
            timestamp,
            startedAt,
            cancellationToken
        );
    }

    private async Task<ManifestOperationResult<bool>> ExecuteProcessAsync(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        DateTimeOffset timestamp,
        long startedAt,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(projectDirectory, invocation, console.IsInteractive),
        };
        var startFailure = await StartProcessAsync(
            process,
            projectDirectory,
            invocation,
            timestamp,
            startedAt
        );
        if (startFailure is not null)
        {
            return startFailure;
        }

        using var outputReadCancellation = new CancellationTokenSource();
        var standardOutput = process.StartInfo.RedirectStandardOutput
            ? ReadBoundedAsync(process.StandardOutput, outputReadCancellation.Token)
            : Task.FromResult(string.Empty);
        var standardError = process.StartInfo.RedirectStandardError
            ? ReadBoundedAsync(process.StandardError, outputReadCancellation.Token)
            : Task.FromResult(string.Empty);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return await CompleteCanceledAsync(
                process,
                standardOutput,
                standardError,
                outputReadCancellation,
                projectDirectory,
                invocation,
                timestamp,
                startedAt
            );
        }

        await WriteOutputAsync(standardOutput, standardError);
        return await CompleteExitedAsync(
            process,
            projectDirectory,
            invocation,
            timestamp,
            startedAt
        );
    }

    private async Task<ManifestOperationResult<bool>?> StartProcessAsync(
        Process process,
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        DateTimeOffset timestamp,
        long startedAt
    )
    {
        try
        {
            return process.Start()
                ? null
                : await CompleteAttemptAsync(
                    projectDirectory,
                    invocation,
                    timestamp,
                    startedAt,
                    "start-failed",
                    exitCode: null,
                    ManifestOperationResult<bool>.Failure(
                        $"Unable to start lifecycle hook for pack '{invocation.Invocation.Pack.Manifest.Id}'."
                    )
                );
        }
        catch (Win32Exception exception)
        {
            return await CompleteAttemptAsync(
                projectDirectory,
                invocation,
                timestamp,
                startedAt,
                "start-failed",
                exitCode: null,
                ManifestOperationResult<bool>.Failure(
                    $"Unable to start lifecycle hook executable '{invocation.Executable}': {exception.Message}"
                )
            );
        }
    }

    private async Task<ManifestOperationResult<bool>> CompleteCanceledAsync(
        Process process,
        Task<string> standardOutput,
        Task<string> standardError,
        CancellationTokenSource outputReadCancellation,
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        DateTimeOffset timestamp,
        long startedAt
    )
    {
        await StopAsync(process);
        outputReadCancellation.Cancel();
        CloseRedirectedOutput(process);
        await WriteOutputAsync(standardOutput, standardError);
        return await CompleteAttemptAsync(
            projectDirectory,
            invocation,
            timestamp,
            startedAt,
            "canceled",
            process.HasExited ? process.ExitCode : null,
            ManifestOperationResult<bool>.Failure("Lifecycle hook execution was canceled.")
        );
    }

    private async Task<ManifestOperationResult<bool>> CompleteExitedAsync(
        Process process,
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        DateTimeOffset timestamp,
        long startedAt
    )
    {
        if (process.ExitCode != 0)
        {
            return await CompleteAttemptAsync(
                projectDirectory,
                invocation,
                timestamp,
                startedAt,
                "failed",
                process.ExitCode,
                ManifestOperationResult<bool>.Failure(
                    $"Lifecycle hook '{LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook)}' for pack '{invocation.Invocation.Pack.Manifest.Id}' exited with code {process.ExitCode}."
                )
            );
        }

        console.Success(
            $"Lifecycle command completed in {CliDuration.Format(Stopwatch.GetElapsedTime(startedAt))}"
        );
        console.Info(string.Empty);
        return await CompleteAttemptAsync(
            projectDirectory,
            invocation,
            timestamp,
            startedAt,
            "succeeded",
            process.ExitCode,
            ManifestOperationResult<bool>.Success(true)
        );
    }

    private ManifestOperationResult<bool> VerifyPackedFile(
        ResolvedLifecycleHookInvocation invocation
    )
    {
        if (invocation.Invocation.PackedFile is not { } packedFile)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        var verified = packedFile.Verify(fileSystem);
        return verified.IsSuccess
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(
                verified.Error ?? "Packed lifecycle hook file integrity verification failed."
            );
    }

    internal static ProcessStartInfo CreateStartInfo(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        bool isInteractive
    )
    {
        var startInfo = new ProcessStartInfo(invocation.Executable)
        {
            CreateNoWindow = !isInteractive,
            RedirectStandardError = !isInteractive,
            RedirectStandardOutput = !isInteractive,
            UseShellExecute = false,
            WorkingDirectory = projectDirectory,
        };
        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private void WriteOutput(string output, bool isError)
    {
        var sanitized = Sanitize(output);
        if (sanitized.Length > 0)
        {
            if (isError)
            {
                console.WarningLines(sanitized);
            }
            else
            {
                console.InfoLines(sanitized);
            }
        }
    }

    private async Task WriteOutputAsync(Task<string> standardOutput, Task<string> standardError)
    {
        WriteOutput(await standardOutput, isError: false);
        WriteOutput(await standardError, isError: true);
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken
    )
    {
        var buffer = new char[4096];
        var output = new StringBuilder(MaximumOutputCharacters);
        while (true)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return output.ToString();
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                return output.ToString();
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return output.ToString();
            }

            if (read == 0)
            {
                return output.ToString();
            }

            output.Append(buffer, 0, Math.Min(read, MaximumOutputCharacters - output.Length));
        }
    }

    private static void CloseRedirectedOutput(Process process)
    {
        if (process.StartInfo.RedirectStandardOutput)
        {
            process.StandardOutput.Dispose();
        }

        if (process.StartInfo.RedirectStandardError)
        {
            process.StandardError.Dispose();
        }
    }

    private static string Sanitize(string output) =>
        new([
            .. output.Where(character =>
                character is '\n' or '\r' or '\t' || !char.IsControl(character)
            ),
        ]);

    private async Task<ManifestOperationResult<bool>> CompleteAttemptAsync(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        DateTimeOffset timestamp,
        long startedAt,
        string outcome,
        int? exitCode,
        ManifestOperationResult<bool> result
    )
    {
        if (historyStore is null)
        {
            return result;
        }

        if (invocation.Authorization is not { } authorization)
        {
            console.Warning(
                "Lifecycle hook history was not recorded because its authorization source is unavailable."
            );
            return result;
        }

        var entry = new LifecycleHookHistoryEntry
        {
            Version = LifecycleHookHistoryEntry.CurrentVersion,
            Timestamp = timestamp.ToUniversalTime(),
            Event = LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook),
            Executable = SanitizeProjectPath(invocation.Executable, projectDirectory),
            PackId = invocation.Invocation.Pack.Manifest.Id,
            PackVersion = invocation.Invocation.Pack.Manifest.Version,
            Arguments = SanitizeArguments(projectDirectory, invocation),
            AuthorizationSource = FormatAuthorizationSource(authorization.Kind),
            TrustScopes = [.. authorization.TrustScopes.Select(FormatTrustScope)],
            DurationMilliseconds = Math.Max(
                0,
                Convert.ToInt64(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds)
            ),
            Outcome = outcome,
            ExitCode = exitCode,
        };
        ManifestOperationResult<bool> saved;
        try
        {
            saved = await historyStore.AppendAsync(entry);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or SecurityException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            console.Warning($"Unable to record lifecycle hook history: {exception.Message}");
            return result;
        }

        if (!saved.IsSuccess)
        {
            console.Warning(
                $"Unable to record lifecycle hook history: {saved.Error ?? "history write failed."}"
            );
        }

        return result;
    }

    private IReadOnlyList<string> SanitizeArguments(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation
    )
    {
        var arguments = invocation.Invocation.PackedFile is { } packedFile
            ? [packedFile.RelativePath, .. invocation.Invocation.Script.Arguments]
            : invocation.Invocation.Script.Arguments;
        return [.. arguments.Select(argument => SanitizeProjectPath(argument, projectDirectory))];
    }

    private string SanitizeProjectPath(string value, string projectDirectory)
    {
        var fullProjectDirectory = fileSystem.Path.TrimEndingDirectorySeparator(
            fileSystem.Path.GetFullPath(projectDirectory)
        );
        var normalizedProjectDirectory = ProjectPath.Normalize(fullProjectDirectory);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var sanitized = value.Replace(fullProjectDirectory, "[PROJECT]", comparison);
        return string.Equals(
            fullProjectDirectory,
            normalizedProjectDirectory,
            StringComparison.Ordinal
        )
            ? sanitized
            : sanitized.Replace(normalizedProjectDirectory, "[PROJECT]", comparison);
    }

    private static string FormatAuthorizationSource(LifecycleScriptAuthorizationKind kind) =>
        kind switch
        {
            LifecycleScriptAuthorizationKind.InvocationRun => "scripts-run",
            LifecycleScriptAuthorizationKind.PersistedTrust => "persisted-trust",
            LifecycleScriptAuthorizationKind.InteractiveConfirmation => "interactive-confirmation",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static string FormatTrustScope(TrustScope scope) =>
        scope switch
        {
            TrustScope.Project => "project",
            TrustScope.LocalUser => "local-user",
            TrustScope.GlobalUser => "global-user",
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };

    private static async Task StopAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync();
    }
}
