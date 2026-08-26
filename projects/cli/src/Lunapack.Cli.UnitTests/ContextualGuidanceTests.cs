using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests;

public sealed class ContextualGuidanceTests
{
    [Test]
    public async Task Root_WhenWorkspaceMissing_RecommendsInitialization()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);

        var exitCode = await workspace.Application.RunAsync([], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("No LunaPack workspace found.");
        await Assert.That(ansiConsole.Output).Contains("luna init");
        await Assert.That(ansiConsole.Output).Contains(ProjectStateStore.ConfigurationFileName);
        await Assert.That(ansiConsole.Output).Contains(ProjectStateStore.LockFileName);
    }

    [Test]
    public async Task Root_WhenWorkspaceMatures_ChangesRecommendedCommands()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var emptyOutput = await InvokeWithFreshConsoleAsync(workspace, []);
        CreatePack(workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        var sourcedOutput = await InvokeWithFreshConsoleAsync(workspace, []);
        await workspace.Application.RunAsync(["install", "example"], workspace.Path);
        var activeOutput = await InvokeWithFreshConsoleAsync(workspace, []);

        await Assert.That(emptyOutput).Contains("No sources are configured.");
        await Assert.That(emptyOutput).Contains("luna sources add git");
        await Assert.That(sourcedOutput).Contains("Configured sources: 1");
        await Assert.That(sourcedOutput).Contains("luna discover");
        await Assert.That(activeOutput).Contains("Installed packs: 1");
        await Assert.That(activeOutput).Contains("luna outdated");
    }

    [Test]
    public async Task Root_WhenWorkspaceOptionProvided_InspectsSelectedWorkspace()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var ansiConsole = new SpectreTestConsole();
        var application = new CliApplication(workspace.FileSystem, ansiConsole);

        var exitCode = await application.RunAsync(
            ["--workspace", workspace.Path],
            Path.GetTempPath()
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("Workspace detected.");
        await Assert.That(ansiConsole.Output).Contains("No sources are configured.");
    }

    [Test]
    public async Task Root_WhenWorkspaceStateInvalid_ReturnsFailureWithoutGuidance()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        File.WriteAllText(
            Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName),
            "schemaVersion: 1\nsources: []\npacks: []\ntrust:\n  sources: []\n  packs: []\nvariables: {}\n"
        );

        var exitCode = await workspace.Application.RunAsync([], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).Contains(ProjectStateStore.LockFileName);
        await Assert.That(ansiConsole.Output).DoesNotContain("luna init");
    }

    [Test]
    public async Task Discover_WhenWorkspaceMissing_RendersRecoveryWithoutCreatingState()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);

        var exitCode = await workspace.Application.RunAsync(["discover"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).Contains("No LunaPack workspace found.");
        await Assert.That(ansiConsole.Output).Contains("luna init");
        await Assert
            .That(
                File.Exists(Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName))
            )
            .IsFalse();
    }

    [Test]
    public async Task Install_WhenPackUnavailable_RecommendsSearchAndDiscovery()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );

        var output = await InvokeWithFreshConsoleAsync(workspace, ["install", "unknown-pack"]);

        await Assert.That(output).Contains("luna search unknown-pack");
        await Assert.That(output).Contains("luna discover");
        var state = await workspace.StateStore.LoadAsync(workspace.Path);
        await Assert.That(state.RequireValue().Configuration.Packs).IsEmpty();
    }

    [Test]
    public async Task Install_WhenDryRunCompletes_DoesNotRenderInstalledGuidance()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        CreatePack(workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );

        var output = await InvokeWithFreshConsoleAsync(
            workspace,
            ["install", "example", "--dry-run"]
        );

        await Assert.That(output).DoesNotContain("✓ Installed");
        await Assert.That(output).DoesNotContain("luna outdated");
    }

    [Test]
    public async Task Update_WhenDryRunCompletes_DoesNotRenderUpdatedGuidance()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        CreateVersionedPack(workspace.Path, "example-v1", "1.0.0", "one");
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        await workspace.Application.RunAsync(["install", "example"], workspace.Path);
        CreateVersionedPack(workspace.Path, "example-v2", "2.0.0", "two");

        var output = await InvokeWithFreshConsoleAsync(workspace, ["update", "--dry-run"]);

        await Assert.That(output).DoesNotContain("✓ Updated");
        await Assert.That(output).DoesNotContain("luna audit");
    }

    [Test]
    public async Task SourcesAdd_WhenRemoteVariantsSucceed_RenderOrderedGuidance()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);

        var gitOutput = await InvokeWithFreshConsoleAsync(
            workspace,
            ["sources", "add", "git", "git", "https://example.test/packs.git"]
        );
        var githubOutput = await InvokeWithFreshConsoleAsync(
            workspace,
            ["sources", "add", "github", "github", "acme/packs"]
        );

        await Assert.That(gitOutput).Contains("✓ Source 'git' added");
        await Assert.That(gitOutput).Contains("1. Discover available packs");
        await Assert.That(githubOutput).Contains("✓ Source 'github' added");
        await Assert.That(githubOutput).Contains("luna install <pack>");
    }

    [Test]
    public async Task SourcesRm_WhenNameUnknown_PreservesConfiguration()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        CreatePack(workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var originalConfiguration = File.ReadAllText(configurationPath);

        var output = await InvokeWithFreshConsoleAsync(workspace, ["sources", "rm", "unknown"]);

        await Assert.That(output).Contains("Source 'unknown' is not configured.");
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(originalConfiguration);
    }

    [Test]
    public async Task SourcesRm_WhenAnotherSourceRemains_PreservesUnrelatedTrust()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "other"));
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "other", "other"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        state.Configuration.Trust.Sources.AddRange(["local", "other"]);
        await workspace.StateStore.SaveAsync(workspace.Path, state);

        var output = await InvokeWithFreshConsoleAsync(workspace, ["sources", "rm", "local"]);
        var updatedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(output).Contains("luna sources list");
        await Assert.That(output).Contains("luna discover");
        await Assert
            .That(updatedState.Configuration.Sources.Select(source => source.Name))
            .IsEquivalentTo(["other"]);
        await Assert.That(updatedState.Configuration.Trust.Sources).IsEquivalentTo(["other"]);
    }

    [Test]
    public async Task SourcesRm_WhenPersistenceFails_PreservesConfiguration()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        CreatePack(workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var originalConfiguration = File.ReadAllText(configurationPath);
        var console = TestConsole.Create();
        var stateStore = new FailingProjectStateStore(workspace.StateStore);
        var advisor = new NextStepAdvisor(workspace.FileSystem, stateStore);
        var handler = new LocalSourceCommandHandler(
            workspace.FileSystem,
            stateStore,
            new WorkspaceDirectoryResolver(workspace.FileSystem),
            advisor,
            new NextStepRenderer(console),
            console
        );

        var exitCode = await handler.RemoveAsync(workspace.Path, "local");

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(originalConfiguration);
    }

    [Test]
    public async Task SourcesRm_WhenSourceTrustedAndUsed_RetainsPackAndRevokesTrust()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        CreatePack(workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        await workspace.Application.RunAsync(["install", "example"], workspace.Path);
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        state.Configuration.Trust.Sources.Add("local");
        state.Configuration.Trust.Packs.Add(
            new ProjectConfiguration.TrustedPack { Id = "example", Source = "local" }
        );
        await workspace.StateStore.SaveAsync(workspace.Path, state);

        var output = await InvokeWithFreshConsoleAsync(workspace, ["sources", "rm", "local"]);
        var updatedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(output).Contains("No sources remain.");
        await Assert.That(output).Contains("luna sources add git");
        await Assert.That(updatedState.Configuration.Sources).IsEmpty();
        await Assert.That(updatedState.Configuration.Trust.Sources).IsEmpty();
        await Assert.That(updatedState.Configuration.Trust.Packs).IsEmpty();
        await Assert.That(updatedState.Configuration.Packs).Count().IsEqualTo(1);
        await Assert.That(updatedState.LockFile.Packs).Count().IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "example.txt"))).IsTrue();

        Directory.CreateDirectory(Path.Combine(workspace.Path, "replacement"));
        var reboundOutput = await InvokeWithFreshConsoleAsync(
            workspace,
            ["sources", "add", "local", "local", "replacement"]
        );
        var reboundState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        await Assert.That(reboundOutput).Contains("✓ Source 'local' added");
        await Assert.That(reboundState.Configuration.Trust.Sources).IsEmpty();
        await Assert.That(reboundState.Configuration.Trust.Packs).IsEmpty();
        await Assert
            .That(reboundState.LockFile.Packs.Single().SourceIdentity!.Path)
            .IsEqualTo("source");
    }

    [Test]
    public async Task Uninstall_WhenSourceRemovedAndAnotherRootRemains_UsesLockedGraph()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        CreateNamedPack(workspace.Path, "one");
        CreateNamedPack(workspace.Path, "two");
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        await workspace.Application.RunAsync(["install", "one", "two"], workspace.Path);
        await workspace.Application.RunAsync(["sources", "rm", "local"], workspace.Path);

        var output = await InvokeWithFreshConsoleAsync(workspace, ["uninstall", "one"]);
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(output).Contains("✓ Uninstalled one");
        await Assert
            .That(state.Configuration.Packs.Select(pack => pack.Id))
            .IsEquivalentTo(["two"]);
        await Assert.That(state.LockFile.Packs.Select(pack => pack.Id)).IsEquivalentTo(["two"]);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "one.txt"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "two.txt"))).IsTrue();
    }

    private static async Task<string> InvokeWithFreshConsoleAsync(
        TestWorkspace workspace,
        string[] arguments
    )
    {
        var ansiConsole = new SpectreTestConsole();
        var application = new CliApplication(workspace.FileSystem, ansiConsole);
        await application.RunAsync(arguments, workspace.Path);
        return ansiConsole.Output;
    }

    private static void CreatePack(string workspacePath)
    {
        CreateNamedPack(workspacePath, "example");
    }

    private static void CreateNamedPack(string workspacePath, string packId)
    {
        var packDirectory = Path.Combine(workspacePath, "source", packId);
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templatesDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            $"id: {packId}\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: {packId}.txt\n"
        );
        File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), packId);
    }

    private static void CreateVersionedPack(
        string workspacePath,
        string directory,
        string version,
        string content
    )
    {
        var packDirectory = Path.Combine(workspacePath, "source", directory);
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templatesDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            $"id: example\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: example.txt\n"
        );
        File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), content);
    }
}
