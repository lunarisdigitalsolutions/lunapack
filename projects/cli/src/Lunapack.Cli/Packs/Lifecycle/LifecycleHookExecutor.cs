using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed class LifecycleHookExecutor(IFileSystem fileSystem, CliConsole console)
{
    private const int MaximumOutputCharacters = 64 * 1024;
    private const int AbortTimeoutSeconds = 5;
    private static readonly TimeSpan _abortTimeout = TimeSpan.FromSeconds(AbortTimeoutSeconds);
    private static readonly TimeSpan _outputDrainTimeout = TimeSpan.FromSeconds(5);

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

        if (cancellationToken.IsCancellationRequested)
        {
            return LifecycleHookExecutionResult.Cancelled("Lifecycle hook execution was canceled.");
        }

        var resultPath = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            $"lunapack-lifecycle-{Guid.NewGuid():N}.result"
        );
        var controlPath = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            $"lunapack-lifecycle-{Guid.NewGuid():N}.control"
        );
        try
        {
            PrepareResultPath(resultPath);
            PrepareResultPath(controlPath);
            return await ExecutePreparedAsync(
                projectDirectory,
                invocation,
                policy,
                resultPath,
                controlPath,
                cancellationToken
            );
        }
        catch (Win32Exception exception)
        {
            return LifecycleHookExecutionResult.Failure(
                $"Unable to start lifecycle hook executable '{invocation.Executable}': {exception.Message}"
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return LifecycleHookExecutionResult.Failure(
                $"Unable to prepare lifecycle hook execution: {exception.Message}"
            );
        }
        finally
        {
            if (fileSystem.File.Exists(resultPath))
            {
                fileSystem.File.Delete(resultPath);
            }
            if (fileSystem.File.Exists(controlPath))
            {
                fileSystem.File.Delete(controlPath);
            }
        }
    }

    private async Task<LifecycleHookExecutionResult> ExecutePreparedAsync(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        EffectiveLifecycleHookPolicy policy,
        string resultPath,
        string controlPath,
        CancellationToken cancellationToken
    )
    {
        var startedAt = Stopwatch.GetTimestamp();
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
                $"Unable to start lifecycle hook for pack '{invocation.Invocation.Pack.Manifest.Id}'."
            );
        }

        var standardOutput = process.StartInfo.RedirectStandardOutput
            ? ReadBoundedAsync(process.StandardOutput)
            : Task.FromResult(string.Empty);
        var standardError = process.StartInfo.RedirectStandardError
            ? ReadBoundedAsync(process.StandardError)
            : Task.FromResult(string.Empty);
        var interrupted = await WaitForProcessAsync(
            process,
            standardOutput,
            standardError,
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

        WriteOutput(await standardOutput, isError: false);
        WriteOutput(await standardError, isError: true);
        var exitCode = ReadHookExitCode(resultPath, invocation);
        if (exitCode.Failure is not null)
        {
            return exitCode.Failure;
        }

        if (exitCode.Value != 0)
        {
            return LifecycleHookExecutionResult.Failure(
                $"Lifecycle hook '{LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook)}' for pack '{invocation.Invocation.Pack.Manifest.Id}' exited with code {exitCode.Value}."
            );
        }

        console.Success(
            $"Lifecycle command completed in {CliDuration.Format(Stopwatch.GetElapsedTime(startedAt))}"
        );
        console.Info(string.Empty);
        return LifecycleHookExecutionResult.Success();
    }

    private async Task<LifecycleHookExecutionResult?> WaitForProcessAsync(
        Process process,
        Task<string> standardOutput,
        Task<string> standardError,
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
        ResolvedLifecycleHookInvocation invocation,
        int timeoutSeconds,
        string resultPath,
        string controlPath,
        CancellationToken cancellationToken
    )
    {
        await RequestAbortAsync(controlPath);
        var cleanupError = await StopAsync(process);
        if (cleanupError is not null)
        {
            return LifecycleHookExecutionResult.Failure(cleanupError);
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
                $"Unable to drain lifecycle hook output for pack '{invocation.Invocation.Pack.Manifest.Id}' after terminating its process tree."
            );
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return LifecycleHookExecutionResult.Failure(
                $"Unable to drain lifecycle hook output after terminating its process tree: {exception.Message}"
            );
        }

        WriteOutput(await standardOutput, isError: false);
        WriteOutput(await standardError, isError: true);
        return cancellationToken.IsCancellationRequested
            ? LifecycleHookExecutionResult.Cancelled("Lifecycle hook execution was canceled.")
            : LifecycleHookExecutionResult.Timeout(
                $"Lifecycle hook '{LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook)}' for pack '{invocation.Invocation.Pack.Manifest.Id}' exceeded its {timeoutSeconds}-second timeout."
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
                console.Warning(sanitized);
            }
            else
            {
                console.Info(sanitized);
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var output = new StringBuilder(MaximumOutputCharacters);
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                return output.ToString();
            }

            output.Append(buffer, 0, Math.Min(read, MaximumOutputCharacters - output.Length));
        }
    }

    private static string Sanitize(string output) =>
        new([
            .. output.Where(character =>
                character is '\n' or '\r' or '\t' || !char.IsControl(character)
            ),
        ]);

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
                $"Lifecycle hook supervisor failed for executable '{invocation.Executable}': {value[LifecycleHookSupervisor.FailurePrefix.Length..]}"
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
