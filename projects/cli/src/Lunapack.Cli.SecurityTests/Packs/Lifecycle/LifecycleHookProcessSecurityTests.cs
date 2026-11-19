using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;
using Spectre.Console.Testing;

namespace Lunapack.Cli.SecurityTests.Packs.Lifecycle;

public sealed class LifecycleHookProcessSecurityTests
{
    [Test]
    public async Task Scenario_HookExitsNonzero_ReportsActualExitCode()
    {
        await RunInWorkspaceAsync(async projectDirectory =>
        {
            var (executor, _) = CreateExecutor();

            var result = await executor.ExecuteAsync(
                projectDirectory,
                CreateInvocation(ShellArgument, FailureCommand),
                CreatePolicy()
            );

            await Assert.That(result.Outcome).IsEqualTo(LifecycleHookOutcome.Failed);
            await Assert.That(result.Error).Contains("exited with code 7");
        });
    }

    [Test]
    public async Task Scenario_HookExceedsTimeout_ReturnsTimeout()
    {
        await RunInWorkspaceAsync(async projectDirectory =>
        {
            var (executor, _) = CreateExecutor();

            var result = await executor.ExecuteAsync(
                projectDirectory,
                CreateInvocation(ShellArgument, LongRunningCommand),
                CreatePolicy(timeoutSeconds: 1)
            );

            await Assert
                .That(result.Outcome)
                .IsEqualTo(LifecycleHookOutcome.TimedOut)
                .Because(result.Error ?? "No lifecycle error was returned.");
            await Assert.That(result.Error).Contains("exceeded its 1-second timeout");
        });
    }

    [Test]
    public async Task Scenario_HookInheritsSecret_WarnsByNameWithoutValue()
    {
        await RunInWorkspaceAsync(async projectDirectory =>
        {
            var secretName = CreateEnvironmentVariableName("SECRET");
            var outputName = CreateEnvironmentVariableName("OUTPUT");
            var secretValue = $"secret-{Guid.NewGuid():N}";
            var outputPath = Path.Combine(projectDirectory, "inherited.txt");
            var (executor, console) = CreateExecutor();
            await WithEnvironmentVariableAsync(
                secretName,
                secretValue,
                () =>
                    WithEnvironmentVariableAsync(
                        outputName,
                        outputPath,
                        async () =>
                        {
                            var result = await executor.ExecuteAsync(
                                projectDirectory,
                                CreateEnvironmentCaptureInvocation(secretName, outputName),
                                CreatePolicy()
                            );

                            await Assert.That(result.Error).IsNull();
                            await Assert.That(console.Output).Contains(secretName);
                            await Assert.That(console.Output).DoesNotContain(secretValue);
                            await Assert
                                .That(File.ReadAllText(outputPath).Trim())
                                .IsEqualTo(secretValue);
                        }
                    )
            );
        });
    }

    [Test]
    public async Task Scenario_EnvironmentNameContainsTerminalEscape_IsRenderedAsText()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await RunInWorkspaceAsync(async projectDirectory =>
        {
            var name = $"LUNAPACK_TEST_{Guid.NewGuid():N}\u001B[31m";
            var (executor, console) = CreateExecutor();
            await WithEnvironmentVariableAsync(
                name,
                "value",
                async () =>
                {
                    var result = await executor.ExecuteAsync(
                        projectDirectory,
                        CreateInvocation(ShellArgument, "exit 0"),
                        CreatePolicy()
                    );

                    await Assert.That(result.IsSuccess).IsTrue();
                    var renderedWarning = console.Output.ReplaceLineEndings(string.Empty);
                    await Assert.That(renderedWarning).Contains("\\u001B[31m");
                    await Assert.That(renderedWarning).DoesNotContain(name);
                }
            );
        });
    }

    [Test]
    public async Task Scenario_CallerCancelsRunningHook_TerminatesDescendant()
    {
        await RunInWorkspaceAsync(async projectDirectory =>
        {
            var markerName = CreateEnvironmentVariableName("STARTED");
            var processIdName = CreateEnvironmentVariableName("CHILD_PID_FILE");
            var markerPath = Path.Combine(projectDirectory, "started.txt");
            var processIdPath = Path.Combine(projectDirectory, "child.pid");
            await WithEnvironmentVariableAsync(
                markerName,
                markerPath,
                () =>
                    WithEnvironmentVariableAsync(
                        processIdName,
                        processIdPath,
                        async () =>
                        {
                            var (executor, _) = CreateExecutor();
                            using var cancellation = new CancellationTokenSource();
                            try
                            {
                                var execution = executor.ExecuteAsync(
                                    projectDirectory,
                                    CreateRunningDescendantInvocation(markerName, processIdName),
                                    CreatePolicy(),
                                    cancellation.Token
                                );
                                await WaitForFileAsync(markerPath);
                                var childProcessId = ReadProcessId(processIdPath);
                                cancellation.Cancel();

                                var result = await execution;

                                await Assert
                                    .That(result.Outcome)
                                    .IsEqualTo(LifecycleHookOutcome.Cancelled);
                                await WaitForProcessExitAsync(childProcessId);
                            }
                            finally
                            {
                                await StopProcessIfRunningAsync(processIdPath);
                            }
                        }
                    )
            );
        });
    }

    [Test]
    public async Task Scenario_RootExitsBeforeTimeout_TerminatesRemainingDescendant()
    {
        await RunInWorkspaceAsync(async projectDirectory =>
        {
            var processIdName = CreateEnvironmentVariableName("CHILD_PID_FILE");
            var processIdPath = Path.Combine(projectDirectory, "child.pid");
            await WithEnvironmentVariableAsync(
                processIdName,
                processIdPath,
                async () =>
                {
                    var (executor, _) = CreateExecutor();
                    try
                    {
                        var execution = executor.ExecuteAsync(
                            projectDirectory,
                            CreateExitedParentDescendantInvocation(processIdName),
                            CreatePolicy(timeoutSeconds: 5)
                        );
                        await WaitForFileAsync(processIdPath, timeoutSeconds: 10);
                        var childProcessId = ReadProcessId(processIdPath);

                        var result = await execution;

                        await Assert.That(result.Outcome).IsEqualTo(LifecycleHookOutcome.TimedOut);
                        await WaitForProcessExitAsync(childProcessId);
                    }
                    finally
                    {
                        await StopProcessIfRunningAsync(processIdPath);
                    }
                }
            );
        });
    }

    [Test]
    public async Task Scenario_LinuxHookStartsDetachedDescendant_TerminationStillReachesIt()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/setsid"))
        {
            return;
        }

        await RunInWorkspaceAsync(async projectDirectory =>
        {
            var processIdName = CreateEnvironmentVariableName("CHILD_PID_FILE");
            var processIdPath = Path.Combine(projectDirectory, "child.pid");
            await WithEnvironmentVariableAsync(
                processIdName,
                processIdPath,
                async () =>
                {
                    var (executor, _) = CreateExecutor();
                    try
                    {
                        var execution = executor.ExecuteAsync(
                            projectDirectory,
                            CreateDetachedDescendantInvocation(processIdName),
                            CreatePolicy(timeoutSeconds: 2)
                        );
                        await WaitForFileAsync(processIdPath);
                        var childProcessId = ReadProcessId(processIdPath);

                        var result = await execution;

                        await Assert.That(result.Outcome).IsEqualTo(LifecycleHookOutcome.TimedOut);
                        await WaitForProcessExitAsync(childProcessId);
                    }
                    finally
                    {
                        await StopProcessIfRunningAsync(processIdPath);
                    }
                }
            );
        });
    }

    [Test]
    public async Task Scenario_MacHookStartsDetachedDescendant_TerminationStillReachesIt()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/perl"))
        {
            return;
        }

        await RunInWorkspaceAsync(async projectDirectory =>
        {
            var processIdName = CreateEnvironmentVariableName("CHILD_PID_FILE");
            var processIdPath = Path.Combine(projectDirectory, "child.pid");
            await WithEnvironmentVariableAsync(
                processIdName,
                processIdPath,
                async () =>
                {
                    var (executor, _) = CreateExecutor();
                    try
                    {
                        var execution = executor.ExecuteAsync(
                            projectDirectory,
                            CreateMacDetachedDescendantInvocation(processIdName),
                            CreatePolicy(timeoutSeconds: 2)
                        );
                        await WaitForFileAsync(processIdPath);
                        var childProcessId = ReadProcessId(processIdPath);

                        var result = await execution;

                        await Assert.That(result.Outcome).IsEqualTo(LifecycleHookOutcome.TimedOut);
                        await WaitForProcessExitAsync(childProcessId);
                    }
                    finally
                    {
                        await StopProcessIfRunningAsync(processIdPath);
                    }
                }
            );
        });
    }

    private static (
        LifecycleHookExecutor Executor,
        Spectre.Console.Testing.TestConsole Console
    ) CreateExecutor()
    {
        var console = new Spectre.Console.Testing.TestConsole();
        console.Profile.Capabilities.Interactive = false;
        return (
            new LifecycleHookExecutor(new FileSystem(), new CliConsole(console, CliLogLevel.Info)),
            console
        );
    }

    private static EffectiveLifecycleHookPolicy CreatePolicy(
        int timeoutSeconds = LifecycleHookPolicyResolver.DefaultTimeoutSeconds
    )
    {
        var defaults = LifecycleHookPolicyResolver.Resolve(null, null, null);
        return defaults with { TimeoutSeconds = timeoutSeconds };
    }

    private static ResolvedLifecycleHookInvocation CreateInvocation(params string[] arguments) =>
        CreateInvocationForExecutable(ShellExecutable, arguments);

    private static ResolvedLifecycleHookInvocation CreateInvocationForExecutable(
        string executable,
        params string[] arguments
    )
    {
        var pack = new DiscoveredPack(
            "source",
            "source/example",
            new PackManifest { Id = "example", Version = "1.0.0" },
            "local",
            ConfiguredSourceIdentity.CreateLocal("source")
        );
        return new ResolvedLifecycleHookInvocation(
            new LifecycleHookInvocation(
                pack,
                LifecycleHook.PreInstall,
                new PackManifest.PackHook
                {
                    Type = "script",
                    Command = executable,
                    Arguments = [.. arguments],
                },
                null
            ),
            executable
        );
    }

    private static ResolvedLifecycleHookInvocation CreateEnvironmentCaptureInvocation(
        string secretName,
        string outputName
    ) =>
        OperatingSystem.IsWindows()
            ? CreateInvocationForExecutable(
                WindowsPowerShellExecutable,
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                $"[IO.File]::WriteAllText($env:{outputName}, $env:{secretName})"
            )
            : CreateInvocation(ShellArgument, $"printf '%s' \"${secretName}\" > \"${outputName}\"");

    private static ResolvedLifecycleHookInvocation CreateRunningDescendantInvocation(
        string markerName,
        string processIdName
    )
    {
        if (!OperatingSystem.IsWindows())
        {
            return CreateInvocation(
                ShellArgument,
                $"sleep 30 & child=$!; printf '%s' \"$child\" > \"${processIdName}\"; printf ready > \"${markerName}\"; wait \"$child\""
            );
        }

        return CreateWindowsDescendantInvocation(
            $"$child = Start-Process -NoNewWindow -FilePath $env:ComSpec -ArgumentList @('/c', 'ping -n 30 127.0.0.1 > nul') -PassThru; [IO.File]::WriteAllText($env:{processIdName}, $child.Id.ToString()); [IO.File]::WriteAllText($env:{markerName}, 'ready'); Wait-Process -Id $child.Id"
        );
    }

    private static ResolvedLifecycleHookInvocation CreateExitedParentDescendantInvocation(
        string processIdName
    )
    {
        if (!OperatingSystem.IsWindows())
        {
            return CreateInvocation(
                ShellArgument,
                $"sleep 30 & child=$!; printf '%s' \"$child\" > \"${processIdName}\"; exit 0"
            );
        }

        return CreateWindowsDescendantInvocation(
            $"$child = Start-Process -NoNewWindow -FilePath $env:ComSpec -ArgumentList @('/c', 'ping -n 30 127.0.0.1 > nul') -PassThru; [IO.File]::WriteAllText($env:{processIdName}, $child.Id.ToString())"
        );
    }

    private static ResolvedLifecycleHookInvocation CreateDetachedDescendantInvocation(
        string processIdName
    ) =>
        CreateInvocation(
            ShellArgument,
            $"/usr/bin/setsid sh -c 'printf %s $$ > \"${processIdName}\"; sleep 30' &"
        );

    private static ResolvedLifecycleHookInvocation CreateMacDetachedDescendantInvocation(
        string processIdName
    )
    {
        var perlScript =
            "POSIX::setsid(); open(F, \">\", $ENV{"
            + processIdName
            + "}); print F $$; close(F); sleep 30";
        return CreateInvocation(ShellArgument, $"/usr/bin/perl -MPOSIX -e '{perlScript}' &");
    }

    private static ResolvedLifecycleHookInvocation CreateWindowsDescendantInvocation(
        string command
    ) =>
        CreateInvocationForExecutable(
            WindowsPowerShellExecutable,
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            command
        );

    private static async Task RunInWorkspaceAsync(Func<string, Task> action)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "lunapack-lifecycle-security-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            await action(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WithEnvironmentVariableAsync(
        string name,
        string value,
        Func<Task> action
    )
    {
        var previousValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previousValue);
        }
    }

    private static int ReadProcessId(string processIdPath) =>
        int.Parse(File.ReadAllText(processIdPath), CultureInfo.InvariantCulture);

    private static async Task WaitForFileAsync(string path, int timeoutSeconds = 5)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        while (!File.Exists(path))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private static async Task WaitForProcessExitAsync(int processId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (IsProcessRunning(processId))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private static async Task StopProcessIfRunningAsync(string processIdPath)
    {
        if (!File.Exists(processIdPath))
        {
            return;
        }

        var processId = ReadProcessId(processIdPath);
        if (!IsProcessRunning(processId))
        {
            return;
        }

        using var process = Process.GetProcessById(processId);
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return;
        }

        await process.WaitForExitAsync();
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string CreateEnvironmentVariableName(string suffix) =>
        $"LUNAPACK_TEST_{suffix}_{Guid.NewGuid():N}".ToUpperInvariant();

    private static string ShellExecutable =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : "/bin/sh";

    private static string ShellArgument => OperatingSystem.IsWindows() ? "/c" : "-c";

    private static string LongRunningCommand =>
        OperatingSystem.IsWindows() ? "ping -n 10 127.0.0.1 > nul" : "sleep 10";

    private static string FailureCommand => OperatingSystem.IsWindows() ? "exit /b 7" : "exit 7";

    private static string WindowsPowerShellExecutable =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
}
