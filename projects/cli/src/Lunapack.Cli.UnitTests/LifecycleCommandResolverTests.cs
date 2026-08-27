using System.IO.Abstractions;

namespace Lunapack.Cli.UnitTests;

[NotInParallel]
public sealed class LifecycleCommandResolverTests
{
    [Test]
    public async Task Resolve_WindowsExtensionlessCommand_UsesPathExtExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new TestWorkspace();
        var extensionlessPath = Path.Combine(workspace.Path, "npm");
        var commandPath = Path.Combine(workspace.Path, "npm.cmd");
        File.WriteAllText(extensionlessPath, "#!/bin/sh");
        File.WriteAllText(commandPath, "@echo off");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalPathExt = Environment.GetEnvironmentVariable("PATHEXT");

        try
        {
            Environment.SetEnvironmentVariable("PATH", workspace.Path);
            Environment.SetEnvironmentVariable("PATHEXT", ".CMD;.EXE");

            var result = new LifecycleCommandResolver(new FileSystem()).Resolve(
                CreateInvocation("npm")
            );

            await Assert
                .That(result.RequireValue().Executable)
                .IsEqualTo(commandPath, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("PATHEXT", originalPathExt);
        }
    }

    private static LifecycleHookInvocation CreateInvocation(string command) =>
        new(
            new DiscoveredPack(
                "source",
                "source/example",
                new PackManifest { Id = "example", Version = "1.0.0" },
                "local",
                ConfiguredSourceIdentity.CreateLocal("source")
            ),
            LifecycleHook.PostInstall,
            new PackManifest.LifecycleScript { Command = command },
            null
        );
}
