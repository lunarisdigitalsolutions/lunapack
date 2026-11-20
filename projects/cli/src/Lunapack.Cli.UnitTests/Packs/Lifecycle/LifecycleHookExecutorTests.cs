using System.Diagnostics;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Packs.Lifecycle;

public sealed class LifecycleHookExecutorTests
{
    [Test]
    public async Task CreateStartInfo_WhenConsoleIsInteractive_InheritsTerminalStreams()
    {
        using var workspace = new TestWorkspace();

        var startInfo = LifecycleHookExecutor.CreateStartInfo(
            workspace.Path,
            CreateInvocation(),
            isInteractive: true
        );

        await Assert.That(startInfo.RedirectStandardInput).IsFalse();
        await Assert.That(startInfo.RedirectStandardOutput).IsFalse();
        await Assert.That(startInfo.RedirectStandardError).IsFalse();
        await Assert.That(startInfo.CreateNoWindow).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_WhenHookExitsNonzero_ReturnsFailureContext()
    {
        using var workspace = new TestWorkspace();
        var store = CreateHistoryStore(workspace);
        var result = await CreateExecutor(store)
            .ExecuteAsync(workspace.Path, CreateInvocation(ShellArgument, FailureCommand));
        var entry = (await store.LoadAsync()).RequireValue().Single();

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("exited with code 7");
        await Assert.That(entry.Outcome).IsEqualTo("failed");
        await Assert.That(entry.ExitCode).IsEqualTo(7);
    }

    [Test]
    public async Task ExecuteAsync_WhenCanceled_TerminatesProcessTreeAndReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = CreateHistoryStore(workspace);

        var result = await CreateExecutor(store)
            .ExecuteAsync(
                workspace.Path,
                CreateInvocation(ShellArgument, LongRunningCommand),
                cancellation.Token
            );
        var entry = (await store.LoadAsync()).RequireValue().Single();

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("canceled");
        await Assert.That(entry.Outcome).IsEqualTo("canceled");
        await Assert.That(entry.ExitCode).IsNotNull();
    }

    [Test]
    public async Task ExecuteAsync_WhenCanceledAndDetachedDescendantRetainsOutput_ReturnsPromptly()
    {
        const string pythonExecutable = "/usr/bin/python3";
        if (!OperatingSystem.IsLinux() || !File.Exists(pythonExecutable))
        {
            Skip.Test("This regression test requires Linux and /usr/bin/python3.");
        }

        using var workspace = new TestWorkspace();
        var scriptPath = Path.Combine(workspace.Path, "detached-output.py");
        var readyPath = scriptPath + ".ready";
        await File.WriteAllTextAsync(
            scriptPath,
            """
            import os
            import pathlib
            import time

            first_child = os.fork()
            if first_child == 0:
                os.setsid()
                second_child = os.fork()
                if second_child > 0:
                    os._exit(0)
                pathlib.Path(__file__ + ".ready").write_text("ready")
                print("detached child output", flush=True)
                time.sleep(4)
                os._exit(0)

            os.waitpid(first_child, 0)
            time.sleep(30)
            """
        );
        using var cancellation = new CancellationTokenSource();
        var execution = CreateExecutor()
            .ExecuteAsync(
                workspace.Path,
                CreateInvocationWithExecutable(pythonExecutable, scriptPath),
                cancellation.Token
            );
        var setup = Stopwatch.StartNew();
        while (
            !File.Exists(readyPath)
            && !execution.IsCompleted
            && setup.Elapsed < TimeSpan.FromSeconds(5)
        )
        {
            await Task.Delay(10);
        }

        if (!File.Exists(readyPath))
        {
            cancellation.Cancel();
            await execution;
            await Assert.That(File.Exists(readyPath)).IsTrue();
            return;
        }

        var canceled = Stopwatch.StartNew();
        cancellation.Cancel();
        var result = await execution;
        canceled.Stop();

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("canceled");
        await Assert.That(canceled.Elapsed < TimeSpan.FromSeconds(3)).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_WhenProcessCannotStart_RecordsStartFailure()
    {
        using var workspace = new TestWorkspace();
        var store = CreateHistoryStore(workspace);
        var executable = Path.Combine(workspace.Path, "missing-tool");

        var result = await CreateExecutor(store)
            .ExecuteAsync(workspace.Path, CreateInvocationWithExecutable(executable));
        var entry = (await store.LoadAsync()).RequireValue().Single();

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(entry.Outcome).IsEqualTo("start-failed");
        await Assert.That(entry.ExitCode).IsNull();
        await Assert.That(entry.Executable).DoesNotContain(workspace.Path);
    }

    [Test]
    public async Task ExecuteAsync_WhenHookSucceeds_RecordsMetadataWithoutProjectPath()
    {
        using var workspace = new TestWorkspace();
        var store = CreateHistoryStore(workspace);
        var invocation = CreateInvocation(
            ShellArgument,
            SuccessCommand,
            "--project",
            workspace.Path
        );

        var result = await CreateExecutor(store).ExecuteAsync(workspace.Path, invocation);
        var entry = (await store.LoadAsync()).RequireValue().Single();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(entry.Timestamp.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(entry.Event).IsEqualTo("preInstall");
        await Assert.That(entry.PackId).IsEqualTo("example");
        await Assert.That(entry.PackVersion).IsEqualTo("1.0.0");
        await Assert.That(entry.Outcome).IsEqualTo("succeeded");
        await Assert.That(entry.ExitCode).IsEqualTo(0);
        await Assert.That(entry.AuthorizationSource).IsEqualTo("scripts-run");
        await Assert.That(entry.DurationMilliseconds).IsGreaterThanOrEqualTo(0);
        await Assert.That(entry.Arguments).Contains("[PROJECT]");
        await Assert.That(entry.Executable).DoesNotContain(workspace.Path);
        await Assert.That(File.ReadAllText(store.HistoryPath)).DoesNotContain(workspace.Path);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteAsync_WhenHistoryWriteFails_PreservesHookResult(bool hookFails)
    {
        using var workspace = new TestWorkspace();
        var store = CreateHistoryStore(workspace);
        Directory.CreateDirectory(workspace.UserSettingsStore.SettingsDirectory);
        UserSettingsPathSecurity.Apply(
            workspace.UserSettingsStore.SettingsDirectory,
            directory: true
        );
        Directory.CreateDirectory(store.HistoryPath);
        var ansiConsole = new SpectreTestConsole();
        var console = new CliConsole(ansiConsole, CliLogLevel.Info);
        var command = hookFails ? FailureCommand : SuccessCommand;

        var result = await new LifecycleHookExecutor(
            workspace.FileSystem,
            console,
            store
        ).ExecuteAsync(workspace.Path, CreateInvocation(ShellArgument, command));

        await Assert.That(result.IsSuccess).IsEqualTo(!hookFails);
        await Assert.That(ansiConsole.Output).Contains("Unable to record lifecycle hook history");
    }

    [Test]
    public async Task ExecuteAsync_WhenOutputContainsLines_PreservesLineBreaks()
    {
        using var workspace = new TestWorkspace();
        var ansiConsole = new SpectreTestConsole();
        var command = OperatingSystem.IsWindows()
            ? "(echo first & echo second)"
            : "printf 'first\\nsecond\\n'";
        var result = await new LifecycleHookExecutor(
            workspace.FileSystem,
            new CliConsole(ansiConsole, CliLogLevel.Info)
        ).ExecuteAsync(workspace.Path, CreateInvocation(ShellArgument, command));

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(ansiConsole.Output).Contains("first");
        await Assert.That(ansiConsole.Output).Contains("second");
        await Assert.That(ansiConsole.Output).DoesNotContain("u000a");
        await Assert.That(ansiConsole.Output).DoesNotContain("u000d");
    }

    private static LifecycleHookExecutor CreateExecutor(
        LifecycleHookHistoryStore? historyStore = null
    ) => new(new FileSystem(), TestConsole.Create(), historyStore);

    private static LifecycleHookHistoryStore CreateHistoryStore(TestWorkspace workspace) =>
        new(workspace.FileSystem, workspace.UserSettingsStore.SettingsDirectory);

    private static ResolvedLifecycleHookInvocation CreateInvocation(params string[] arguments)
    {
        return CreateInvocationWithExecutable(ShellExecutable, arguments);
    }

    private static ResolvedLifecycleHookInvocation CreateInvocationWithExecutable(
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
            executable,
            LifecycleScriptAuthorizationSource.InvocationRun
        );
    }

    private static string ShellExecutable =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : "/bin/sh";

    private static string ShellArgument => OperatingSystem.IsWindows() ? "/c" : "-c";

    private static string FailureCommand => OperatingSystem.IsWindows() ? "exit /b 7" : "exit 7";

    private static string SuccessCommand => OperatingSystem.IsWindows() ? "exit /b 0" : "exit 0";

    private static string LongRunningCommand =>
        OperatingSystem.IsWindows() ? "ping -n 10 127.0.0.1 > nul" : "sleep 10";
}
