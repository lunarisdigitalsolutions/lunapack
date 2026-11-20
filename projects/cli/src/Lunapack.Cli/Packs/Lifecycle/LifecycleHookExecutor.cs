using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
    private const int AbortTimeoutSeconds = 5;
    private static readonly TimeSpan _abortTimeout = TimeSpan.FromSeconds(AbortTimeoutSeconds);
    private static readonly TimeSpan _outputDrainTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<LifecycleHookExecutionResult> ExecuteAsync(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        CancellationToken cancellationToken = default
    ) =>
        ExecuteAsync(
            projectDirectory,
            invocation,
            LifecycleHookPolicyResolver.Resolve(null, null, null),
            cancellationToken
        );

    public async Task<LifecycleHookExecutionResult> ExecuteAsync(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        EffectiveLifecycleHookPolicy policy,
        CancellationToken cancellationToken = default
    )
    {
        var integrity = VerifyPackedFile(invocation);
        if (!integrity.IsSuccess)
        {
            return LifecycleHookExecutionResult.Failure(
                integrity.Error ?? "Packed lifecycle hook file integrity verification failed."
            );
        }

        var timestamp = _timeProvider.GetUtcNow();
        var startedAt = Stopwatch.GetTimestamp();
        var resultPath = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            $"lunapack-lifecycle-{Guid.NewGuid():N}.result"
        );
        var controlPath = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            $"lunapack-lifecycle-{Guid.NewGuid():N}.control"
        );
        LifecycleHookExecutionResult result;
        try
        {
            PrepareResultPath(resultPath);
            PrepareResultPath(controlPath);
            result = await ExecutePreparedAsync(
                projectDirectory,
                invocation,
                policy,
                resultPath,
                controlPath,
                startedAt,
                cancellationToken
            );
        }
        catch (Win32Exception exception)
        {
            result = LifecycleHookExecutionResult.Failure(
                $"Unable to start lifecycle hook executable '{invocation.Executable}': {exception.Message}",
                historyOutcome: "start-failed"
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result = LifecycleHookExecutionResult.Failure(
                $"Unable to prepare lifecycle hook execution: {exception.Message}",
                historyOutcome: "start-failed"
            );
        }
        finally
        {
            DeleteTemporaryFile(resultPath);
            DeleteTemporaryFile(controlPath);
        }

        return await CompleteAttemptAsync(
            projectDirectory,
            invocation,
            timestamp,
            startedAt,
            result
        );
    }

    private async Task<LifecycleHookExecutionResult> ExecutePreparedAsync(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        EffectiveLifecycleHookPolicy policy,
        string resultPath,
        string controlPath,
        long startedAt,
        CancellationToken cancellationToken
    )
    {
        WarnAboutUnallowedEnvironmentVariables(invocation, policy);
        using var process = new Process
        {
            StartInfo = CreateSupervisorStartInfo(
                projectDirectory,
                invocation,
                resultPath,
                controlPath
            ),
        };
        if (!process.Start())
        {
            return LifecycleHookExecutionResult.Failure(
                $"Unable to start lifecycle hook for pack '{invocation.Invocation.Pack.Manifest.Id}'.",
                historyOutcome: "start-failed"
            );
        }

        using var outputReadCancellation = new CancellationTokenSource();
        var standardOutput = process.StartInfo.RedirectStandardOutput
            ? ReadBoundedAsync(process.StandardOutput, outputReadCancellation.Token)
            : Task.FromResult(string.Empty);
        var standardError = process.StartInfo.RedirectStandardError
            ? ReadBoundedAsync(process.StandardError, outputReadCancellation.Token)
            : Task.FromResult(string.Empty);
        var interrupted = await WaitForProcessAsync(
            process,
            standardOutput,
            standardError,
            outputReadCancellation,
            invocation,
            policy.TimeoutSeconds,
            resultPath,
            controlPath,
            cancellationToken
        );
        if (interrupted is not null)
        {
            return interrupted;
        }

        await WriteOutputAsync(standardOutput, standardError);
        var exitCode = ReadHookExitCode(resultPath, invocation);
        if (exitCode.Failure is not null)
        {
            return exitCode.Failure;
        }

        if (exitCode.Value != 0)
        {
            return LifecycleHookExecutionResult.Failure(
                $"Lifecycle hook '{LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook)}' for pack '{invocation.Invocation.Pack.Manifest.Id}' exited with code {exitCode.Value}.",
                exitCode.Value
            );
        }

        console.Success(
            $"Lifecycle command completed in {CliDuration.Format(Stopwatch.GetElapsedTime(startedAt))}"
        );
        console.Info(string.Empty);
        return LifecycleHookExecutionResult.Success(exitCode.Value);
    }

    private async Task<LifecycleHookExecutionResult?> WaitForProcessAsync(
        Process process,
        Task<string> standardOutput,
        Task<string> standardError,
        CancellationTokenSource outputReadCancellation,
        ResolvedLifecycleHookInvocation invocation,
        int timeoutSeconds,
        string resultPath,
        string controlPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await Task.WhenAll(
                    process.WaitForExitAsync(cancellationToken),
                    standardOutput,
                    standardError
                )
                .WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
            return null;
        }
        catch (TimeoutException)
        {
            return await StopAndReportAsync(
                process,
                standardOutput,
                standardError,
                outputReadCancellation,
                invocation,
                timeoutSeconds,
                resultPath,
                controlPath,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await StopAndReportAsync(
                process,
                standardOutput,
                standardError,
                outputReadCancellation,
                invocation,
                timeoutSeconds,
                resultPath,
                controlPath,
                cancellationToken
            );
        }
    }

    private async Task<LifecycleHookExecutionResult> StopAndReportAsync(
        Process process,
        Task<string> standardOutput,
        Task<string> standardError,
        CancellationTokenSource outputReadCancellation,
        ResolvedLifecycleHookInvocation invocation,
        int timeoutSeconds,
        string resultPath,
        string controlPath,
        CancellationToken cancellationToken
    )
    {
        await RequestAbortAsync(controlPath);
        var cleanupError = await StopAsync(process);
        outputReadCancellation.Cancel();
        CloseRedirectedOutput(process);
        if (cleanupError is not null)
        {
            return LifecycleHookExecutionResult.Failure(
                cleanupError,
                process.HasExited ? process.ExitCode : null
            );
        }

        var supervisorFailure = ReadSupervisorFailure(resultPath, invocation);
        if (supervisorFailure is not null)
        {
            return supervisorFailure;
        }

        try
        {
            await Task.WhenAll(standardOutput, standardError)
                .WaitAsync(_outputDrainTimeout, CancellationToken.None);
        }
        catch (TimeoutException)
        {
            return LifecycleHookExecutionResult.Failure(
                $"Unable to drain lifecycle hook output for pack '{invocation.Invocation.Pack.Manifest.Id}' after terminating its process tree.",
                process.HasExited ? process.ExitCode : null
            );
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return LifecycleHookExecutionResult.Failure(
                $"Unable to drain lifecycle hook output after terminating its process tree: {exception.Message}",
                process.HasExited ? process.ExitCode : null
            );
        }

        await WriteOutputAsync(standardOutput, standardError);
        int? exitCode = process.HasExited ? process.ExitCode : null;
        return cancellationToken.IsCancellationRequested
            ? LifecycleHookExecutionResult.Cancelled(
                "Lifecycle hook execution was canceled.",
                exitCode
            )
            : LifecycleHookExecutionResult.Timeout(
                $"Lifecycle hook '{LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook)}' for pack '{invocation.Invocation.Pack.Manifest.Id}' exceeded its {timeoutSeconds}-second timeout.",
                exitCode
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

    private void WarnAboutUnallowedEnvironmentVariables(
        ResolvedLifecycleHookInvocation invocation,
        EffectiveLifecycleHookPolicy policy
    )
    {
        var unexpectedVariables = Environment
            .GetEnvironmentVariables()
            .Keys.OfType<string>()
            .Where(name => !policy.AllowedEnvironmentVariables.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unexpectedVariables.Length == 0)
        {
            return;
        }

        console.Warning(
            $"Lifecycle hook '{LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook)}' for pack '{invocation.Invocation.Pack.Manifest.Id}' inherits environment variables outside the allow-list: {string.Join(", ", unexpectedVariables.Select(EscapeEnvironmentVariableName))}. Values remain available to the script."
        );
    }

    private static string EscapeEnvironmentVariableName(string name)
    {
        var escapedName = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (
                char.GetUnicodeCategory(character)
                is UnicodeCategory.Control
                    or UnicodeCategory.Format
                    or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator
                    or UnicodeCategory.Surrogate
            )
            {
                escapedName.Append("\\u");
                escapedName.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                escapedName.Append(character);
            }
        }

        return escapedName.ToString();
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

    internal static ProcessStartInfo CreateInheritedStartInfo(
        string projectDirectory,
        string executable,
        IReadOnlyList<string> arguments
    )
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            CreateNoWindow = false,
            RedirectStandardError = false,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            WorkingDirectory = projectDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    internal ProcessStartInfo CreateSupervisorStartInfo(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        string resultPath,
        string controlPath
    )
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "luna.dll");
        var runsFromAssembly = fileSystem.File.Exists(assemblyPath);
        var fileName = runsFromAssembly ? GetDotnetHostPath() : Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Unable to locate the lifecycle hook supervisor.");
        }

        var startInfo = new ProcessStartInfo(fileName)
        {
            CreateNoWindow = !console.IsInteractive,
            RedirectStandardError = !console.IsInteractive,
            RedirectStandardOutput = !console.IsInteractive,
            UseShellExecute = false,
            WorkingDirectory = projectDirectory,
        };
        if (runsFromAssembly)
        {
            startInfo.ArgumentList.Add(assemblyPath);
        }

        startInfo.ArgumentList.Add(LifecycleHookSupervisor.CommandName);
        startInfo.ArgumentList.Add(projectDirectory);
        startInfo.ArgumentList.Add(resultPath);
        startInfo.ArgumentList.Add(controlPath);
        startInfo.ArgumentList.Add(invocation.Executable);
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
        try
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
        catch (ObjectDisposedException) { }
    }

    private static string Sanitize(string output) =>
        new([
            .. output.Where(character =>
                character is '\n' or '\r' or '\t' || !char.IsControl(character)
            ),
        ]);

    private async Task<LifecycleHookExecutionResult> CompleteAttemptAsync(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        DateTimeOffset timestamp,
        long startedAt,
        LifecycleHookExecutionResult result
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
            Outcome =
                result.HistoryOutcome
                ?? (
                    result.Outcome switch
                    {
                        LifecycleHookOutcome.Succeeded => "succeeded",
                        LifecycleHookOutcome.Cancelled => "canceled",
                        _ => "failed",
                    }
                ),
            ExitCode = result.ExitCode,
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

    private static async Task<string?> StopAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(_abortTimeout);
            return null;
        }
        catch (TimeoutException) { }
        catch (InvalidOperationException) when (process.HasExited) { }

        try
        {
            LifecycleHookContainment.Kill(process);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return $"Unable to terminate lifecycle hook process tree: {exception.Message}";
        }

        try
        {
            await process.WaitForExitAsync();
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return $"Unable to reap lifecycle hook process tree: {exception.Message}";
        }
    }

    private HookExitCodeResult ReadHookExitCode(
        string resultPath,
        ResolvedLifecycleHookInvocation invocation
    )
    {
        if (!fileSystem.File.Exists(resultPath))
        {
            return HookExitCodeResult.Failed(
                $"Lifecycle hook supervisor for pack '{invocation.Invocation.Pack.Manifest.Id}' did not report an exit code."
            );
        }

        var value = fileSystem.File.ReadAllText(resultPath);
        var supervisorFailure = ReadSupervisorFailure(resultPath, invocation);
        if (supervisorFailure is not null)
        {
            return new(null, supervisorFailure);
        }

        var successValue = value.StartsWith(
            LifecycleHookSupervisor.SuccessPrefix,
            StringComparison.Ordinal
        )
            ? value[LifecycleHookSupervisor.SuccessPrefix.Length..]
            : value;
        return int.TryParse(
            successValue,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var code
        )
            ? new(code, null)
            : HookExitCodeResult.Failed(
                $"Lifecycle hook supervisor for pack '{invocation.Invocation.Pack.Manifest.Id}' reported an invalid exit code."
            );
    }

    private LifecycleHookExecutionResult? ReadSupervisorFailure(
        string resultPath,
        ResolvedLifecycleHookInvocation invocation
    )
    {
        if (!fileSystem.File.Exists(resultPath))
        {
            return null;
        }

        var value = fileSystem.File.ReadAllText(resultPath);
        return value.StartsWith(LifecycleHookSupervisor.FailurePrefix, StringComparison.Ordinal)
            ? LifecycleHookExecutionResult.Failure(
                $"Lifecycle hook supervisor failed for executable '{invocation.Executable}': {value[LifecycleHookSupervisor.FailurePrefix.Length..]}",
                historyOutcome: "start-failed"
            )
            : null;
    }

    private void PrepareResultPath(string resultPath)
    {
        fileSystem
            .File.Open(resultPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)
            .Dispose();
        UserSettingsPathSecurity.Apply(resultPath, directory: false);
    }

    private async Task RequestAbortAsync(string controlPath)
    {
        try
        {
            await fileSystem.File.WriteAllTextAsync(
                controlPath,
                LifecycleHookSupervisor.AbortCommand,
                CancellationToken.None
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Process-tree termination below is the fallback when control-file I/O fails.
        }
    }

    private void DeleteTemporaryFile(string path)
    {
        if (fileSystem.File.Exists(path))
        {
            fileSystem.File.Delete(path);
        }
    }

    private static string GetDotnetHostPath()
    {
        var configuredHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (
            !string.IsNullOrWhiteSpace(configuredHostPath)
            && string.Equals(
                Path.GetFileNameWithoutExtension(configuredHostPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return configuredHostPath;
        }

        return "dotnet";
    }
}
