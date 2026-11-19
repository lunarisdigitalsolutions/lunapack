using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;
using Spectre.Console;

namespace Lunapack.Cli.SecurityTests.Packs.Lifecycle;

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

    [Test]
    public async Task Confirm_WhenPackTextContainsTerminalControls_RendersVisibleEscapes()
    {
        const string commandArgument =
            "\u001b[2J\u001b]0;forged\u0007\rRun this script? [y/N]\n\u2028\u202e";
        const string description = "description\u0008\u200f";
        var invocation = CreateInvocation([commandArgument], description);
        var output = new StringWriter();
        var ansiConsole = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(output),
            }
        );

        var confirmed = new ConsoleLifecycleHookConfirmer(
            new CliConsole(ansiConsole, CliLogLevel.Info)
        ).Confirm(invocation);
        var rendered = output.ToString();

        await Assert.That(confirmed).IsFalse();
        await Assert.That(rendered).DoesNotContain("\u001b");
        await Assert.That(rendered).DoesNotContain("\u0007");
        await Assert.That(rendered).DoesNotContain("\u2028");
        await Assert.That(rendered).DoesNotContain("\u202e");
        await Assert.That(rendered).Contains("u001b");
        await Assert.That(rendered).Contains("u000d");
        await Assert.That(rendered).Contains("u202e");
        await Assert
            .That(
                LifecycleHookExecutor
                    .CreateStartInfo(Environment.CurrentDirectory, invocation, isInteractive: false)
                    .ArgumentList
            )
            .IsEquivalentTo([commandArgument]);
    }

    [Test]
    public async Task LoadAsync_WhenHistoryFileIsSymbolicLink_RejectsAliasedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lunapack-history-{Guid.NewGuid():N}");
        var settingsDirectory = Path.Combine(root, ".lunapack");
        var target = Path.Combine(root, "outside.jsonl");
        var historyPath = Path.Combine(settingsDirectory, LifecycleHookHistoryStore.FileName);
        Directory.CreateDirectory(settingsDirectory);
        UserSettingsPathSecurity.Apply(settingsDirectory, directory: true);
        File.WriteAllText(target, "{}");

        try
        {
            try
            {
                File.CreateSymbolicLink(historyPath, target);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                Skip.Test($"Symbolic links are unavailable: {exception.Message}");
            }

            var result = await new LifecycleHookHistoryStore(
                new FileSystem(),
                settingsDirectory
            ).LoadAsync();

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("cannot be a link or reparse point");
        }
        finally
        {
            if (File.Exists(historyPath))
            {
                File.Delete(historyPath);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ResolvedLifecycleHookInvocation CreateInvocation(
        string[] arguments,
        string? description = null
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
                    Command = "executable",
                    Arguments = [.. arguments],
                    Description = description,
                },
                null
            ),
            "executable"
        );
    }
}
