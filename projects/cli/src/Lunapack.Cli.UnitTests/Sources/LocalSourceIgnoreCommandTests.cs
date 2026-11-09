using Lunapack.Cli;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Sources;

public sealed class LocalSourceIgnoreCommandTests
{
    [Test]
    public async Task Scenario_IgnoreCommand_PersistsIdentityAndDisplaysLocalStatus()
    {
        using var workspace = new TestWorkspace();
        var console = new SpectreTestConsole();
        var settingsStore = CreateSettingsStore(workspace);
        var application = new CliApplication(
            workspace.FileSystem,
            console,
            userSettingsStore: settingsStore
        );
        var sourcePath = Path.Combine(workspace.Path, "source");
        Directory.CreateDirectory(sourcePath);
        await application.RunAsync(["init"], workspace.Path);
        await application.RunAsync(
            ["sources", "add", "local", "upstream", "source"],
            workspace.Path
        );

        var exitCode = await application.RunAsync(
            ["sources", "ignore", "upstream"],
            workspace.Path
        );
        var listExitCode = await application.RunAsync(["sources", "list"], workspace.Path);
        var projectKey = settingsStore.GetProjectKey(workspace.Path).RequireValue();
        var projectSettings = (await settingsStore.LoadAsync()).RequireValue().Projects[projectKey];

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(listExitCode).IsEqualTo(0);
        await Assert
            .That(projectSettings.IgnoredSources)
            .Contains(ConfiguredSourceIdentity.CreateLocal(sourcePath));
        await Assert.That(console.Output).Contains("ignored locally");
    }

    [Test]
    public async Task Scenario_OciSourceAddition_StoresUnreachableRegistryWithoutConnecting()
    {
        using var workspace = new TestWorkspace();
        var console = new SpectreTestConsole();
        var application = new CliApplication(workspace.FileSystem, console);
        await application.RunAsync(["init"], workspace.Path);

        var exitCode = await application.RunAsync(
            [
                "sources",
                "add",
                "oci",
                "private",
                "https://registry.unavailable.test",
                "--path",
                "packs/private",
            ],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        var source = state.Configuration.Sources.OfType<ProjectConfiguration.OciSource>().Single();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.Url).IsEqualTo("https://registry.unavailable.test");
        await Assert.That(source.Path).IsEqualTo("packs/private");
    }

    [Test]
    public async Task Scenario_RenameCommand_PreservesIgnoreStateForSameSourceIdentity()
    {
        using var workspace = new TestWorkspace();
        var console = new SpectreTestConsole();
        var settingsStore = CreateSettingsStore(workspace);
        var application = new CliApplication(
            workspace.FileSystem,
            console,
            userSettingsStore: settingsStore
        );
        var sourcePath = Path.Combine(workspace.Path, "source");
        Directory.CreateDirectory(sourcePath);
        await application.RunAsync(["init"], workspace.Path);
        await application.RunAsync(
            ["sources", "add", "local", "upstream", "source"],
            workspace.Path
        );
        await application.RunAsync(["sources", "ignore", "upstream"], workspace.Path);

        var renameExitCode = await application.RunAsync(
            ["sources", "rename", "upstream", "private"],
            workspace.Path
        );
        var listExitCode = await application.RunAsync(["sources", "list"], workspace.Path);

        await Assert.That(renameExitCode).IsEqualTo(0);
        await Assert.That(listExitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("private");
        await Assert.That(console.Output).Contains("ignored locally");
    }

    [Test]
    public async Task Scenario_IgnoreAndUnignore_AreIdempotentAndPreserveProjectSettings()
    {
        using var workspace = new TestWorkspace();
        var console = new SpectreTestConsole();
        var settingsStore = CreateSettingsStore(workspace);
        var application = new CliApplication(
            workspace.FileSystem,
            console,
            userSettingsStore: settingsStore
        );
        var sourcePath = Path.Combine(workspace.Path, "source");
        Directory.CreateDirectory(sourcePath);
        await application.RunAsync(["init"], workspace.Path);
        await application.RunAsync(
            ["sources", "add", "local", "upstream", "source"],
            workspace.Path
        );

        var projectKey = settingsStore.GetProjectKey(workspace.Path).RequireValue();
        var identity = ConfiguredSourceIdentity.CreateLocal(sourcePath);
        var existingSettings = new LocalProjectTrust
        {
            Acknowledgements = new TrustAcknowledgements { Sources = [identity] },
            Deny = new ScriptDenial { Scripts = true },
            Packs = [new TrustedPackIdentity { Id = "existing-pack", Source = identity }],
            Sources = [identity],
        };
        var savedSettings = await settingsStore.SaveAsync(
            new UserSettings { Projects = { [projectKey] = existingSettings } }
        );

        var firstIgnore = await application.RunAsync(
            ["sources", "ignore", "upstream"],
            workspace.Path
        );
        var repeatedIgnore = await application.RunAsync(
            ["sources", "ignore", "upstream"],
            workspace.Path
        );
        var firstUnignore = await application.RunAsync(
            ["sources", "unignore", "upstream"],
            workspace.Path
        );
        var repeatedUnignore = await application.RunAsync(
            ["sources", "unignore", "upstream"],
            workspace.Path
        );
        var projectSettings = (await settingsStore.LoadAsync()).RequireValue().Projects[projectKey];

        await Assert.That(savedSettings.IsSuccess).IsTrue();
        await Assert.That(firstIgnore).IsEqualTo(0);
        await Assert.That(repeatedIgnore).IsEqualTo(0);
        await Assert.That(firstUnignore).IsEqualTo(0);
        await Assert.That(repeatedUnignore).IsEqualTo(0);
        await Assert.That(projectSettings.IgnoredSources).IsEmpty();
        await Assert.That(projectSettings.Acknowledgements.Sources).Contains(identity);
        await Assert.That(projectSettings.Deny?.Scripts).IsTrue();
        await Assert
            .That(projectSettings.Packs)
            .Contains(new TrustedPackIdentity { Id = "existing-pack", Source = identity });
        await Assert.That(projectSettings.Sources).Contains(identity);
    }

    [Test]
    public async Task Scenario_IgnoreCommand_RejectsUnknownNamesWithoutWritingSettings()
    {
        using var workspace = new TestWorkspace();
        var settingsStore = CreateSettingsStore(workspace);
        var application = new CliApplication(
            workspace.FileSystem,
            TestConsole.CreateAnsiConsole(),
            userSettingsStore: settingsStore
        );
        await application.RunAsync(["init"], workspace.Path);
        var settingsPath = settingsStore.SettingsPath;

        var exitCode = await application.RunAsync(["sources", "ignore", "missing"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.Exists(settingsPath)).IsFalse();
    }

    [Test]
    public async Task Scenario_IgnoreState_IsIsolatedByCanonicalProject()
    {
        using var workspace = new TestWorkspace();
        var settingsStore = CreateSettingsStore(workspace);
        var firstConsole = new SpectreTestConsole();
        var firstApplication = new CliApplication(
            workspace.FileSystem,
            firstConsole,
            userSettingsStore: settingsStore
        );
        var secondConsole = new SpectreTestConsole();
        var secondApplication = new CliApplication(
            workspace.FileSystem,
            secondConsole,
            userSettingsStore: settingsStore
        );
        var secondProject = Path.Combine(workspace.Path, "second");
        Directory.CreateDirectory(secondProject);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        Directory.CreateDirectory(Path.Combine(secondProject, "source"));
        await firstApplication.RunAsync(["init"], workspace.Path);
        await secondApplication.RunAsync(["init"], secondProject);
        await firstApplication.RunAsync(
            ["sources", "add", "local", "upstream", "source"],
            workspace.Path
        );
        await secondApplication.RunAsync(
            ["sources", "add", "local", "upstream", "source"],
            secondProject
        );
        await firstApplication.RunAsync(["sources", "ignore", "upstream"], workspace.Path);

        var listExitCode = await secondApplication.RunAsync(["sources", "list"], secondProject);

        await Assert.That(listExitCode).IsEqualTo(0);
        await Assert.That(secondConsole.Output).DoesNotContain("ignored locally");
        await Assert.That(firstConsole.Output).Contains("ignored locally");
    }

    private static UserSettingsStore CreateSettingsStore(TestWorkspace workspace) =>
        new(workspace.FileSystem, Path.Combine(workspace.Path, "profile"));
}
