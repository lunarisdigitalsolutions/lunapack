using System.IO.Abstractions;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.UnitTests.Packs.Lifecycle;

public sealed class LifecycleHookHistoryStoreTests
{
    [Test]
    public async Task LoadAsync_WhenHistoryDoesNotExist_ReturnsEmptyWithoutCreatingState()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace);

        var entries = await store.LoadAsync();

        await Assert.That(entries.RequireValue()).IsEmpty();
        await Assert
            .That(Directory.Exists(workspace.UserSettingsStore.SettingsDirectory))
            .IsFalse();
    }

    [Test]
    public async Task AppendAsync_WhenHistoryExceedsCapacity_RetainsNewestOneHundredEntries()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace);

        for (var index = 0; index < LifecycleHookHistoryStore.MaximumEntryCount + 5; index++)
        {
            var result = await store.AppendAsync(CreateEntry(index));
            await Assert.That(result.IsSuccess).IsTrue();
        }

        var entries = (await store.LoadAsync()).RequireValue();
        await Assert.That(entries).Count().IsEqualTo(LifecycleHookHistoryStore.MaximumEntryCount);
        await Assert.That(entries[0].PackId).IsEqualTo("pack-5");
        await Assert.That(entries[^1].PackId).IsEqualTo("pack-104");
    }

    [Test]
    public async Task AppendAsync_WhenExistingHistoryIsCorrupt_PreservesOriginalFile()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace);
        CreateSecureSettingsDirectory(workspace);
        const string corruptContents = "not-json\n";
        File.WriteAllText(store.HistoryPath, corruptContents);
        UserSettingsPathSecurity.Apply(store.HistoryPath, directory: false);

        var result = await store.AppendAsync(CreateEntry(1));

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(File.ReadAllText(store.HistoryPath)).IsEqualTo(corruptContents);
    }

    [Test]
    public async Task AppendAsync_WhenWritersRunConcurrently_PreservesEveryEntry()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace);
        var writes = Enumerable
            .Range(0, 20)
            .Select(index => store.AppendAsync(CreateEntry(index)))
            .ToArray();

        var results = await Task.WhenAll(writes);
        var entries = (await store.LoadAsync()).RequireValue();

        await Assert.That(results.All(result => result.IsSuccess)).IsTrue();
        await Assert.That(entries).Count().IsEqualTo(20);
        await Assert
            .That(entries.Select(entry => entry.PackId))
            .IsEquivalentTo(Enumerable.Range(0, 20).Select(index => $"pack-{index}"));
    }

    [Test]
    public async Task AppendAsync_WhenHistoryIsCreated_RestrictsPathsToCurrentUser()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace);

        var result = await store.AppendAsync(CreateEntry(1));

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(
                UserSettingsPathSecurity.ValidateExisting(
                    workspace.FileSystem,
                    workspace.UserSettingsStore.SettingsDirectory,
                    directory: true
                )
            )
            .IsNull();
        await Assert
            .That(
                UserSettingsPathSecurity.ValidateExisting(
                    workspace.FileSystem,
                    store.HistoryPath,
                    directory: false
                )
            )
            .IsNull();
    }

    [Test]
    public async Task AppendAsync_WhenHistoryPathIsDirectory_ReportsWriteFailure()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace);
        CreateSecureSettingsDirectory(workspace);
        Directory.CreateDirectory(store.HistoryPath);

        var result = await store.AppendAsync(CreateEntry(1));

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("must be a regular file");
    }

    [Test]
    public async Task AppendAsync_WhenArgumentsContainSecrets_PersistsOnlyRedactedValues()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace);
        var entry = CreateEntry(
            1,
            ["--token", "secret-one", "API_KEY=secret-two", "--mode", "safe"]
        );

        var result = await store.AppendAsync(entry);
        var persisted = (await store.LoadAsync()).RequireValue().Single();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(persisted.Arguments)
            .IsEquivalentTo([
                "--token",
                LifecycleHookArgumentRedactor.RedactedValue,
                $"API_KEY={LifecycleHookArgumentRedactor.RedactedValue}",
                "--mode",
                "safe",
            ]);
        await Assert.That(File.ReadAllText(store.HistoryPath)).DoesNotContain("secret-one");
        await Assert.That(File.ReadAllText(store.HistoryPath)).DoesNotContain("secret-two");
    }

    [Test]
    public async Task AppendAsync_WhenArgumentsContainEmptyValues_PersistsCompleteVector()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace);
        var arguments = new[] { string.Empty, "  " };

        var result = await store.AppendAsync(CreateEntry(1, arguments));
        var persisted = (await store.LoadAsync()).RequireValue().Single();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(persisted.Arguments).IsEquivalentTo(arguments);
    }

    [Test]
    [Arguments("password")]
    [Arguments("PASSWD")]
    [Arguments("client-secret")]
    [Arguments("token")]
    [Arguments("credential")]
    [Arguments("authorization")]
    [Arguments("auth")]
    [Arguments("apiKey")]
    [Arguments("api-key")]
    [Arguments("api_key")]
    [Arguments("accessKey")]
    [Arguments("access-key")]
    [Arguments("access_key")]
    public async Task Redact_WhenNameIsSecretLike_RedactsSeparateAndAssignedValues(string name)
    {
        var separate = LifecycleHookArgumentRedactor.Redact([$"--{name}", "value"]);
        var assigned = LifecycleHookArgumentRedactor.Redact([$"--{name}=value"]);

        await Assert
            .That(separate)
            .IsEquivalentTo([$"--{name}", LifecycleHookArgumentRedactor.RedactedValue]);
        await Assert
            .That(assigned)
            .IsEquivalentTo([$"--{name}={LifecycleHookArgumentRedactor.RedactedValue}"]);
    }

    [Test]
    public async Task Redact_WhenNameIsNotSecretLike_PreservesArgumentVector()
    {
        var arguments = new[] { "--mode", "safe", "setting=value" };

        var redacted = LifecycleHookArgumentRedactor.Redact(arguments);

        await Assert.That(redacted).IsEquivalentTo(arguments);
    }

    private static LifecycleHookHistoryStore CreateStore(TestWorkspace workspace) =>
        new(workspace.FileSystem, workspace.UserSettingsStore.SettingsDirectory);

    private static LifecycleHookHistoryEntry CreateEntry(
        int index,
        IReadOnlyList<string>? arguments = null
    ) =>
        new()
        {
            Version = LifecycleHookHistoryEntry.CurrentVersion,
            Timestamp = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero).AddSeconds(index),
            Event = "preInstall",
            Executable = "tool",
            PackId = $"pack-{index}",
            PackVersion = "1.0.0",
            Arguments = arguments ?? [],
            AuthorizationSource = "scripts-run",
            TrustScopes = [],
            DurationMilliseconds = index,
            Outcome = "succeeded",
            ExitCode = 0,
        };

    private static void CreateSecureSettingsDirectory(TestWorkspace workspace)
    {
        Directory.CreateDirectory(workspace.UserSettingsStore.SettingsDirectory);
        UserSettingsPathSecurity.Apply(
            workspace.UserSettingsStore.SettingsDirectory,
            directory: true
        );
    }
}
