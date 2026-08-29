namespace Lunapack.Cli.SecurityTests;

public sealed class LifecycleScriptSecurityTests
{
    [Test]
    [Arguments("; remove-item -recurse .")]
    [Arguments("$(whoami)")]
    [Arguments("`id`")]
    [Arguments("| tee captured.txt")]
    public async Task CreateStartInfo_WhenArgumentContainsShellMetacharacters_PreservesLiteralArgument(
        string untrustedArgument
    )
    {
        var invocation = CreateInvocation(["--flag", untrustedArgument]);

        var startInfo = LifecycleHookExecutor.CreateStartInfo(
            Environment.CurrentDirectory,
            invocation,
            isInteractive: false
        );

        await Assert.That(startInfo.UseShellExecute).IsFalse();
        await Assert.That(startInfo.ArgumentList).IsEquivalentTo(["--flag", untrustedArgument]);
    }

    private static ResolvedLifecycleHookInvocation CreateInvocation(string[] arguments)
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
                    Arguments = [.. arguments],
                },
                null
            ),
            "executable"
        );
    }
}
