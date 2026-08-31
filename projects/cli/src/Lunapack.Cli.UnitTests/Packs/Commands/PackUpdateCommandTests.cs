using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Packs.Commands;

public sealed class PackUpdateCommandTests
{
    [Test]
    public async Task Update_PromptDeclinesOneAvailableRoot_UpdatesOnlyConfirmedRoot()
    {
        var packUpdatePrompter = new TestPackUpdatePrompter([false, true]);
        using var workspace = new TestWorkspace(packUpdatePrompter);
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        CreateVersionedPackSource(workspace.Path, "csharpier", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        await workspace.Application.RunAsync(["install", "csharpier@1.0.0"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update", "-p"], workspace.Path);
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(packUpdatePrompter.PromptedIds).IsEquivalentTo(["csharpier", "dotnet"]);
        await Assert
            .That(
                state
                    .LockFile.Packs.Single(pack =>
                        string.Equals(pack.Id, "csharpier", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsEqualTo("1.0.0");
        await Assert
            .That(
                state
                    .LockFile.Packs.Single(pack =>
                        string.Equals(pack.Id, "dotnet", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsEqualTo("2.0.0");
    }

    [Test]
    public async Task Update_NamedVersionlessRequest_UpdatesLatestAndPreservesDestination()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(
            ["install", "dotnet@1.0.0", "-d", "docs/guidance"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "docs", "guidance", "dotnet.txt")))
            .IsEqualTo("2.0.0");
        var projectState = state.RequireValue();
        await Assert.That(projectState.Configuration.Packs.Single().Version).IsNull();
        await Assert
            .That(projectState.Configuration.Packs.Single().Destination)
            .IsEqualTo("docs/guidance");
        await Assert.That(projectState.LockFile.Packs.Single().Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task Scenario_UpdateSucceeds_ReportsManagedFileChanges()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var outputStart = ansiConsole.Output.Length;

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);
        var output = ansiConsole.Output[outputStart..];

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).Contains("File changes");
        await Assert.That(output).Contains("Copy");
        await Assert.That(output).Contains("dotnet.txt");
    }

    [Test]
    public async Task Scenario_UpdateSuppressesFileChanges_HidesManagedFileChanges()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var outputStart = ansiConsole.Output.Length;

        var exitCode = await workspace.Application.RunAsync(
            ["update", "dotnet", "--no-file-changes"],
            workspace.Path
        );
        var output = ansiConsole.Output[outputStart..];

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).DoesNotContain("File changes");
    }

    [Test]
    public async Task Update_MultipleNamedReferences_UpdatesEachRequestedRoot()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        CreateVersionedPackSource(workspace.Path, "csharpier", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(
            ["install", "dotnet@1.0.0", "csharpier@1.0.0"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["update", "csharpier", "dotnet"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(state.RequireValue().LockFile.Packs.Select(pack => pack.Version))
            .IsEquivalentTo(["2.0.0", "2.0.0"]);
    }

    [Test]
    public async Task Update_NamedExplicitRequest_PersistsSelectedManifestVersion()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["update", "dotnet@2.0.0"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        var projectState = state.RequireValue();
        await Assert.That(projectState.Configuration.Packs.Single().Version).IsEqualTo("2.0.0");
        await Assert.That(projectState.LockFile.Packs.Single().Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task Update_NamedExplicitUnavailableVersion_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["update", "dotnet@2.0.0"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Update_NamedVersionlessCurrentRelease_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Update_NamedPreflightFailure_LeavesManagedTargetAndProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        SetManagedFileStrategy(workspace.Path, "dotnet", "2.0.0", "copy", "fail-if-exists");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);
        var targetPath = Path.Combine(workspace.Path, "dotnet.txt");

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("1.0.0");
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Update_NamedUninstalledPack_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Update_AllAvailableRoots_UpdatesCompleteResolvedGraph()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        CreateVersionedPackSource(workspace.Path, "csharpier", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        await workspace.Application.RunAsync(["install", "csharpier@1.0.0"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update"], workspace.Path);
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        var projectState = state.RequireValue();
        await Assert
            .That(
                projectState
                    .LockFile.Packs.Single(pack =>
                        string.Equals(pack.Id, "dotnet", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsEqualTo("2.0.0");
        await Assert
            .That(
                projectState
                    .LockFile.Packs.Single(pack =>
                        string.Equals(pack.Id, "csharpier", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsEqualTo("2.0.0");
        await Assert
            .That(
                projectState
                    .Configuration.Packs.Single(pack =>
                        string.Equals(pack.Id, "dotnet", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsNull();
        await Assert
            .That(
                projectState
                    .Configuration.Packs.Single(pack =>
                        string.Equals(pack.Id, "csharpier", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsNull();
    }

    [Test]
    public async Task Scenario_NamedUpdateDryRun_PreservesTargetStateAndBackup()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        SetManagedFileStrategy(workspace.Path, "dotnet", "2.0.0", "copy", "backup-and-overwrite");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);
        var targetPath = Path.Combine(workspace.Path, "dotnet.txt");

        var exitCode = await workspace.Application.RunAsync(
            ["update", "dotnet", "-D"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("1.0.0");
        await Assert.That(File.Exists($"{targetPath}.1")).IsFalse();
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Scenario_UpdateAllDryRun_PreservesManagedTargetsAndProjectState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        CreateVersionedPackSource(workspace.Path, "csharpier", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        await workspace.Application.RunAsync(["install", "csharpier@1.0.0"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update", "-D"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "dotnet.txt")))
            .IsEqualTo("1.0.0");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "csharpier.txt")))
            .IsEqualTo("1.0.0");
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Scenario_PromptedUpdateDryRun_PreservesDeclinedAndConfirmedRoots()
    {
        var packUpdatePrompter = new TestPackUpdatePrompter([false, true]);
        using var workspace = new TestWorkspace(packUpdatePrompter);
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        CreateVersionedPackSource(workspace.Path, "csharpier", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        await workspace.Application.RunAsync(["install", "csharpier@1.0.0"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update", "-p", "-D"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(packUpdatePrompter.PromptedIds).IsEquivalentTo(["csharpier", "dotnet"]);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "dotnet.txt")))
            .IsEqualTo("1.0.0");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "csharpier.txt")))
            .IsEqualTo("1.0.0");
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    private static async Task ConfigureSourceAsync(TestWorkspace workspace, string sourcePath)
    {
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", sourcePath],
            workspace.Path
        );
    }

    private static string CreateVersionedPackSource(
        string projectDirectory,
        string id,
        params string[] versions
    )
    {
        var sourcePath = Path.Combine(projectDirectory, "source");
        foreach (var version in versions)
        {
            var packDirectory = Path.Combine(sourcePath, $"{id}-{version}");
            var templateDirectory = Path.Combine(packDirectory, "templates");
            Directory.CreateDirectory(templateDirectory);
            File.WriteAllText(
                Path.Combine(packDirectory, "pack.yml"),
                $"id: {id}\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: {id}.txt\n"
            );
            File.WriteAllText(Path.Combine(templateDirectory, "content.txt"), version);
        }

        return "source";
    }

    private static void SetManagedFileStrategy(
        string projectDirectory,
        string id,
        string version,
        string type,
        string method
    )
    {
        var manifestPath = Path.Combine(projectDirectory, "source", $"{id}-{version}", "pack.yml");
        File.WriteAllText(
            manifestPath,
            $"id: {id}\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: {id}.txt\n    strategy:\n      type: {type}\n      method: {method}\n"
        );
    }

    private static async Task<string> ReadStateAsync(string projectDirectory) =>
        string.Concat(
            await File.ReadAllTextAsync(
                Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName)
            ),
            await File.ReadAllTextAsync(
                Path.Combine(projectDirectory, ProjectStateStore.LockFileName)
            )
        );

    private sealed class TestPackUpdatePrompter(IEnumerable<bool> responses) : IPackUpdatePrompter
    {
        private readonly Queue<bool> _responses = new(responses);

        public List<string> PromptedIds { get; } = [];

        public bool Confirm(AvailablePackUpdate update)
        {
            PromptedIds.Add(update.RequestedRoot.Id);
            return _responses.Dequeue();
        }
    }
}
