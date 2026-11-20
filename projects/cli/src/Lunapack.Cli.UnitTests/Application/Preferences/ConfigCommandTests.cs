using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Application.Preferences;

public sealed class ConfigCommandTests
{
    [Test]
    public async Task Set_WhenScopeOmitted_UsesGlobalScopeAndListShowsConfiguredValue()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var store = CreateUserSettingsStore(workspace);

        var setExitCode = await workspace.Application.RunAsync(
            ["config", "set", "discover.only-composites", "true"],
            workspace.Path
        );
        var listExitCode = await workspace.Application.RunAsync(
            ["config", "list", "--global"],
            workspace.Path
        );
        var settings = await store.LoadAsync();

        await Assert.That(setExitCode).IsEqualTo(0);
        await Assert.That(listExitCode).IsEqualTo(0);
        await Assert
            .That(settings.RequireValue().Global.Preferences?.Discover?.OnlyComposites)
            .IsTrue();
        await Assert.That(ansiConsole.Output).Contains("discover.only-composites=true");
    }

    [Test]
    public async Task Set_WhenGlobalResourceLimitConfigured_ListShowsNumericValue()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        const string key = "resource-limits.pack-manifest-bytes";
        const string value = "2097152";

        var setExitCode = await workspace.Application.RunAsync(
            ["config", "set", key, value],
            workspace.Path
        );
        var listExitCode = await workspace.Application.RunAsync(
            ["config", "list", "--global"],
            workspace.Path
        );
        var settings = await workspace.UserSettingsStore.LoadAsync();

        await Assert.That(setExitCode).IsEqualTo(0);
        await Assert.That(listExitCode).IsEqualTo(0);
        await Assert
            .That(settings.RequireValue().Global.Preferences?.ResourceLimits?.PackManifestBytes)
            .IsEqualTo(2097152);
        await Assert.That(ansiConsole.Output).Contains($"{key}={value}");
    }

    [Test]
    public async Task Set_WhenProjectScopeSelected_UpdatesOnlyProjectConfiguration()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var store = CreateUserSettingsStore(workspace);

        var exitCode = await workspace.Application.RunAsync(
            ["config", "set", "search.only-composites", "false", "--project"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);
        var userSettings = await store.LoadAsync();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(state.RequireValue().Configuration.Preferences?.Search?.OnlyComposites)
            .IsFalse();
        await Assert.That(userSettings.RequireValue().Global.Preferences).IsNull();
        await Assert.That(userSettings.RequireValue().Projects).IsEmpty();
    }

    [Test]
    public async Task Set_WhenUserProjectScopeSelected_UsesCanonicalKeyAndPreservesTrust()
    {
        using var workspace = new TestWorkspace();
        var store = CreateUserSettingsStore(workspace);
        var source = ConfiguredSourceIdentity.CreateLocal(workspace.Path);
        var initialSettings = new UserSettings
        {
            Global = new UserTrust
            {
                Deny = new ScriptDenial { Scripts = true },
                Sources = [source],
            },
        };
        var saved = await store.SaveAsync(initialSettings);

        var exitCode = await workspace.Application.RunAsync(
            ["config", "set", "search.only-composites", "true", "--user-project"],
            workspace.Path
        );
        var key = store.GetProjectKey(workspace.Path).RequireValue();
        var settings = await store.LoadAsync();

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(settings.RequireValue().Projects.ContainsKey(key)).IsTrue();
        await Assert
            .That(settings.RequireValue().Projects[key].Preferences?.Search?.OnlyComposites)
            .IsTrue();
        await Assert.That(settings.RequireValue().Global.Deny?.Scripts).IsTrue();
        await Assert.That(settings.RequireValue().Global.Sources).Contains(source);
    }

    [Test]
    public async Task Set_WhenScopesConflictOrValueIsInvalid_ReturnsFailureWithoutWriting()
    {
        using var workspace = new TestWorkspace();

        var conflictExitCode = await workspace.Application.RunAsync(
            ["config", "set", "discover.only-composites", "true", "--global", "--local"],
            workspace.Path
        );
        var invalidExitCode = await workspace.Application.RunAsync(
            ["config", "set", "discover.only-composites", "yes"],
            workspace.Path
        );
        var settings = await CreateUserSettingsStore(workspace).LoadAsync();

        await Assert.That(conflictExitCode).IsEqualTo(1);
        await Assert.That(invalidExitCode).IsEqualTo(1);
        await Assert.That(settings.RequireValue().Global.Preferences).IsNull();
        await Assert.That(settings.RequireValue().Projects).IsEmpty();
    }

    [Test]
    public async Task Set_WhenProjectResourceLimitExceedsDefault_ReturnsFailureWithoutWriting()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["config", "set", "resource-limits.pack-manifest-bytes", "1048577", "--project"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(state.RequireValue().Configuration.Preferences?.ResourceLimits).IsNull();
    }

    [Test]
    public async Task Remove_WhenPreferenceIsConfigured_RemovesOnlySelectedScopeValue()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var store = CreateUserSettingsStore(workspace);

        var globalSet = await workspace.Application.RunAsync(
            ["config", "set", "discover.only-composites", "true"],
            workspace.Path
        );
        var localSet = await workspace.Application.RunAsync(
            ["config", "set", "discover.only-composites", "false", "--local"],
            workspace.Path
        );
        var remove = await workspace.Application.RunAsync(
            ["config", "remove", "discover.only-composites", "--local"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);
        var settings = await store.LoadAsync();

        await Assert.That(globalSet).IsEqualTo(0);
        await Assert.That(localSet).IsEqualTo(0);
        await Assert.That(remove).IsEqualTo(0);
        await Assert.That(state.RequireValue().Configuration.Preferences).IsNull();
        await Assert
            .That(settings.RequireValue().Global.Preferences?.Discover?.OnlyComposites)
            .IsTrue();
    }

    private static UserSettingsStore CreateUserSettingsStore(TestWorkspace workspace) =>
        new(workspace.FileSystem, Path.Combine(workspace.Path, "profile"));
}
