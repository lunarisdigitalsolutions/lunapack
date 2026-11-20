using Lunapack.Cli.Application;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Trust;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Packs.Lifecycle;

public sealed class LifecycleHookHistoryCommandTests
{
    [Test]
    public async Task History_WhenFileDoesNotExist_ReportsEmptyWithoutCreatingState()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);

        var exitCode = await workspace.Application.RunAsync(["hooks", "history"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(ansiConsole.Output)
            .Contains("No lifecycle hook history has been recorded");
        await Assert
            .That(Directory.Exists(workspace.UserSettingsStore.SettingsDirectory))
            .IsFalse();
    }

    [Test]
    public async Task History_WhenEntriesExist_ShowsTwentyNewestInReverseOrder()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var store = CreateStore(workspace);
        for (var index = 0; index < 21; index++)
        {
            await store.AppendAsync(CreateEntry(index)).ConfigureAwait(true);
        }

        var exitCode = await workspace.Application.RunAsync(["hooks", "history"], workspace.Path);
        var output = ansiConsole.Output;

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(output.IndexOf("pack-20@", StringComparison.Ordinal))
            .IsGreaterThanOrEqualTo(0);
        await Assert
            .That(output.IndexOf("pack-1@", StringComparison.Ordinal))
            .IsGreaterThanOrEqualTo(0);
        await Assert.That(output).DoesNotContain("pack-0@");
        await Assert
            .That(output.IndexOf("pack-20@", StringComparison.Ordinal))
            .IsLessThan(output.IndexOf("pack-19@", StringComparison.Ordinal));
    }

    [Test]
    public async Task History_WhenLimitIsProvided_ShowsRequestedNewestEntriesWithoutMutation()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var store = CreateStore(workspace);
        for (var index = 0; index < 6; index++)
        {
            await store.AppendAsync(CreateEntry(index)).ConfigureAwait(true);
        }

        var original = await File.ReadAllBytesAsync(store.HistoryPath);
        var exitCode = await workspace.Application.RunAsync(
            ["hooks", "history", "--limit", "2"],
            workspace.Path
        );
        var output = ansiConsole.Output;

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).Contains("pack-5@");
        await Assert.That(output).Contains("pack-4@");
        await Assert.That(output).DoesNotContain("pack-3@");
        await Assert.That(await File.ReadAllBytesAsync(store.HistoryPath)).IsEquivalentTo(original);
    }

    [Test]
    [Arguments("0")]
    [Arguments("101")]
    public async Task History_WhenLimitIsOutsideRange_ReturnsFailure(string limit)
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);

        var exitCode = await workspace.Application.RunAsync(
            ["hooks", "history", "--limit", limit],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).Contains("limit must be from 1 through 100");
    }

    [Test]
    public async Task History_WhenFileIsCorrupt_ReturnsFailureWithoutReplacingIt()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var store = CreateStore(workspace);
        Directory.CreateDirectory(workspace.UserSettingsStore.SettingsDirectory);
        UserSettingsPathSecurity.Apply(
            workspace.UserSettingsStore.SettingsDirectory,
            directory: true
        );
        const string corruptContents = "invalid-json\n";
        await File.WriteAllTextAsync(store.HistoryPath, corruptContents);
        UserSettingsPathSecurity.Apply(store.HistoryPath, directory: false);

        var exitCode = await workspace.Application.RunAsync(["hooks", "history"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).Contains("Unable to read lifecycle hook history");
        await Assert
            .That(await File.ReadAllTextAsync(store.HistoryPath))
            .IsEqualTo(corruptContents);
    }

    [Test]
    public async Task History_WhenPersistedTextContainsControls_RendersEscapesWithoutMutation()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var store = CreateStore(workspace);
        var entry = CreateEntry(1) with
        {
            PackId = "pack\u001b[2J\u202e",
            Arguments = ["--value", "line\r\nforged\u0007"],
        };
        var saved = await store.AppendAsync(entry);
        var original = await File.ReadAllTextAsync(store.HistoryPath);

        var exitCode = await workspace.Application.RunAsync(["hooks", "history"], workspace.Path);

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).DoesNotContain("\u001b");
        await Assert.That(ansiConsole.Output).DoesNotContain("\u202e");
        await Assert.That(ansiConsole.Output).Contains("u001b");
        await Assert.That(await File.ReadAllTextAsync(store.HistoryPath)).IsEqualTo(original);
    }

    [Test]
    public async Task History_WhenTrustAuthorizesExecution_DisplaysEveryScope()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var entry = CreateEntry(1) with
        {
            AuthorizationSource = "persisted-trust",
            TrustScopes = ["project", "local-user", "global-user"],
        };
        await CreateStore(workspace).AppendAsync(entry);

        var exitCode = await workspace.Application.RunAsync(["hooks", "history"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(ansiConsole.Output)
            .Contains("persisted trust (project, local-user, global-user)");
    }

    private static LifecycleHookHistoryStore CreateStore(TestWorkspace workspace) =>
        new(workspace.FileSystem, workspace.UserSettingsStore.SettingsDirectory);

    private static LifecycleHookHistoryEntry CreateEntry(int index) =>
        new()
        {
            Version = LifecycleHookHistoryEntry.CurrentVersion,
            Timestamp = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero).AddSeconds(index),
            Event = "preInstall",
            Executable = "tool",
            PackId = $"pack-{index}",
            PackVersion = "1.0.0",
            Arguments = [],
            AuthorizationSource = "scripts-run",
            TrustScopes = [],
            DurationMilliseconds = 1,
            Outcome = "succeeded",
            ExitCode = 0,
        };
}
