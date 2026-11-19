using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Packs.Lifecycle;

public sealed class LifecycleHookExecutorTests
{
    [Test]
    public async Task Scenario_ConsoleIsInteractive_StartInfoInheritsTerminalStreams()
    {
        var startInfo = LifecycleHookExecutor.CreateInheritedStartInfo(
            Environment.CurrentDirectory,
            "executable",
            []
        );

        await Assert.That(startInfo.RedirectStandardInput).IsFalse();
        await Assert.That(startInfo.RedirectStandardOutput).IsFalse();
        await Assert.That(startInfo.RedirectStandardError).IsFalse();
        await Assert.That(startInfo.CreateNoWindow).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Scenario_ConsoleModeConfiguresSupervisorStreams(bool isInteractive)
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = isInteractive;
        var executor = new LifecycleHookExecutor(
            new FileSystem(),
            new CliConsole(ansiConsole, CliLogLevel.Info)
        );

        var startInfo = executor.CreateSupervisorStartInfo(
            Environment.CurrentDirectory,
            CreateInvocation(),
            Path.Combine(Path.GetTempPath(), "result.txt"),
            Path.Combine(Path.GetTempPath(), "control.txt")
        );

        await Assert.That(startInfo.RedirectStandardOutput).IsEqualTo(!isInteractive);
        await Assert.That(startInfo.RedirectStandardError).IsEqualTo(!isInteractive);
        await Assert.That(startInfo.CreateNoWindow).IsEqualTo(!isInteractive);
    }

    private static ResolvedLifecycleHookInvocation CreateInvocation()
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
                    Command = "executable",
                    Arguments = [],
                },
                null
            ),
            "executable"
        );
    }
}
