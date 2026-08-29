using System.IO.Abstractions;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.UnitTests;

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
        var result = await CreateExecutor()
            .ExecuteAsync(workspace.Path, CreateInvocation(ShellArgument, FailureCommand));

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("exited with code 7");
    }

    [Test]
    public async Task ExecuteAsync_WhenCanceled_TerminatesProcessTreeAndReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await CreateExecutor()
            .ExecuteAsync(
                workspace.Path,
                CreateInvocation(ShellArgument, LongRunningCommand),
                cancellation.Token
            );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("canceled");
    }

    private static LifecycleHookExecutor CreateExecutor() =>
        new(new FileSystem(), TestConsole.Create());

    private static ResolvedLifecycleHookInvocation CreateInvocation(params string[] arguments)
    {
        var executable = ShellExecutable;
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

    private static string ShellExecutable =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : "/bin/sh";

    private static string ShellArgument => OperatingSystem.IsWindows() ? "/c" : "-c";

    private static string FailureCommand => OperatingSystem.IsWindows() ? "exit /b 7" : "exit 7";

    private static string LongRunningCommand =>
        OperatingSystem.IsWindows() ? "ping -n 10 127.0.0.1 > nul" : "sleep 10";
}
