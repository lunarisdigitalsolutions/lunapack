using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;

namespace Lunapack.Cli.Packs.Lifecycle;

internal static class LifecycleHookSupervisor
{
    public const string CommandName = "__lifecycle-hook-supervisor";
    public const string AbortCommand = "abort";
    public const string FailurePrefix = "failure:";
    public const string SuccessPrefix = "success:";

    private const int AbortedExitCode = 1;
    private const int InvalidInvocationExitCode = 125;
    private const int LaunchFailureExitCode = 127;
    private const int MinimumArgumentCount = 5;
    private static readonly TimeSpan _controlPollInterval = TimeSpan.FromMilliseconds(25);

    public static bool IsInvocation(string[] arguments) =>
        arguments.Length > 0 && string.Equals(arguments[0], CommandName, StringComparison.Ordinal);

    public static async Task<int> RunAsync(string[] arguments, IFileSystem fileSystem)
    {
        if (arguments.Length < MinimumArgumentCount)
        {
            return InvalidInvocationExitCode;
        }

        var projectDirectory = arguments[1];
        var resultPath = arguments[2];
        var controlPath = arguments[3];
        var executable = arguments[4];
        var hookArguments = arguments[5..];
        LifecycleHookContainment? containment = null;

        try
        {
            containment = LifecycleHookContainment.Create(fileSystem);
            return await ExecuteHookAsync(
                fileSystem,
                containment,
                projectDirectory,
                resultPath,
                controlPath,
                executable,
                hookArguments
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or Win32Exception
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            return await ReportFailureAsync(fileSystem, resultPath, containment, exception);
        }
        finally
        {
            containment?.Dispose();
        }
    }

    private static async Task<int> ExecuteHookAsync(
        IFileSystem fileSystem,
        LifecycleHookContainment containment,
        string projectDirectory,
        string resultPath,
        string controlPath,
        string executable,
        string[] hookArguments
    )
    {
        if (OperatingSystem.IsMacOS())
        {
            return await ExecuteMacHookAsync(
                fileSystem,
                containment,
                resultPath,
                controlPath,
                executable,
                hookArguments
            );
        }

        using var process = CreateProcess(projectDirectory, executable, hookArguments);
        process.Start();
        using var trackingCancellation = new CancellationTokenSource();
        var trackingTask = containment.TrackMacDescendantsAsync(
            process.Id,
            trackingCancellation.Token
        );
        var exitTask = WaitForProcessExitAsync(process);
        return await CompleteHookAsync(
            fileSystem,
            containment,
            resultPath,
            controlPath,
            exitTask,
            trackingTask,
            trackingCancellation
        );
    }

    private static async Task<int> ExecuteMacHookAsync(
        IFileSystem fileSystem,
        LifecycleHookContainment containment,
        string resultPath,
        string controlPath,
        string executable,
        string[] hookArguments
    )
    {
        var processId = LifecycleHookMacProcess.StartSuspended(executable, hookArguments);
        using var trackingCancellation = new CancellationTokenSource();
        var trackingTask = containment.TrackMacDescendantsAsync(
            processId,
            trackingCancellation.Token
        );
        LifecycleHookMacProcess.Resume(processId);
        return await CompleteHookAsync(
            fileSystem,
            containment,
            resultPath,
            controlPath,
            LifecycleHookMacProcess.WaitForExitAsync(processId),
            trackingTask,
            trackingCancellation
        );
    }

    private static async Task<int> CompleteHookAsync(
        IFileSystem fileSystem,
        LifecycleHookContainment containment,
        string resultPath,
        string controlPath,
        Task<int> exitTask,
        Task trackingTask,
        CancellationTokenSource trackingCancellation
    )
    {
        var abortTask = WaitForAbortAsync(fileSystem, controlPath);
        var completed = await Task.WhenAny(exitTask, abortTask, trackingTask);
        if (completed == trackingTask)
        {
            await trackingTask;
        }

        if (completed == abortTask)
        {
            await StopTrackingAsync(trackingCancellation, trackingTask);
            await containment.AbortAsync();
            return AbortedExitCode;
        }

        var exitCode = await exitTask;
        await WriteSuccessAsync(fileSystem, resultPath, exitCode);
        while (await containment.HasActiveDescendantsAsync(CancellationToken.None))
        {
            if (await IsAbortRequestedAsync(fileSystem, controlPath))
            {
                await StopTrackingAsync(trackingCancellation, trackingTask);
                await containment.AbortAsync();
                return AbortedExitCode;
            }

            await Task.Delay(_controlPollInterval);
        }

        await StopTrackingAsync(trackingCancellation, trackingTask);
        return exitCode;
    }

    private static async Task<int> WaitForProcessExitAsync(Process process)
    {
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static async Task StopTrackingAsync(
        CancellationTokenSource cancellation,
        Task trackingTask
    )
    {
        cancellation.Cancel();
        try
        {
            await trackingTask;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private static async Task<int> ReportFailureAsync(
        IFileSystem fileSystem,
        string resultPath,
        LifecycleHookContainment? containment,
        Exception exception
    )
    {
        await TryWriteFailureAsync(fileSystem, resultPath, exception);
        if (containment is not null)
        {
            try
            {
                await containment.AbortAsync();
            }
            catch (Exception cleanupException)
                when (cleanupException
                        is Win32Exception
                            or IOException
                            or UnauthorizedAccessException
                            or InvalidOperationException
                )
            {
                await TryWriteFailureAsync(
                    fileSystem,
                    resultPath,
                    new InvalidOperationException(
                        $"{exception.Message} Cleanup also failed: {cleanupException.Message}"
                    )
                );
            }
        }

        return LaunchFailureExitCode;
    }

    private static Process CreateProcess(
        string projectDirectory,
        string executable,
        IReadOnlyList<string> arguments
    ) =>
        new()
        {
            StartInfo = LifecycleHookExecutor.CreateInheritedStartInfo(
                projectDirectory,
                executable,
                arguments.ToArray()
            ),
        };

    private static async Task TryWriteFailureAsync(
        IFileSystem fileSystem,
        string resultPath,
        Exception exception
    )
    {
        try
        {
            await fileSystem.File.WriteAllTextAsync(
                resultPath,
                $"{FailurePrefix}{exception.Message}"
            );
        }
        catch (IOException)
        {
            // The caller reports a missing supervisor result as the execution failure.
        }
    }

    private static Task WriteSuccessAsync(
        IFileSystem fileSystem,
        string resultPath,
        int exitCode
    ) =>
        fileSystem.File.WriteAllTextAsync(
            resultPath,
            $"{SuccessPrefix}{exitCode.ToString(CultureInfo.InvariantCulture)}"
        );

    private static async Task WaitForAbortAsync(IFileSystem fileSystem, string controlPath)
    {
        while (!await IsAbortRequestedAsync(fileSystem, controlPath))
        {
            await Task.Delay(_controlPollInterval);
        }
    }

    private static async Task<bool> IsAbortRequestedAsync(
        IFileSystem fileSystem,
        string controlPath
    )
    {
        try
        {
            return string.Equals(
                await fileSystem.File.ReadAllTextAsync(controlPath),
                AbortCommand,
                StringComparison.Ordinal
            );
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
