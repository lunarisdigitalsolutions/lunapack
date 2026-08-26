using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests;

public sealed class PackAuthoringCommandTests
{
    [Test]
    public async Task Init_WhenOptionsProvided_CreatesIdentityOnlyManifest()
    {
        using var workspace = new TestWorkspace();

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example", "--version", "1.2.3"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        var contents = File.ReadAllText(Path.Combine(workspace.Path, PackManifestStore.FileName));
        await Assert.That(contents).Contains("id: example");
        await Assert.That(contents).Contains("version: 1.2.3");
        await Assert.That(ManifestModelValidator.Validate(await LoadAsync(workspace))).IsEmpty();
    }

    [Test]
    public async Task Init_WhenManifestExists_PreservesOriginalBytes()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        const string original = "id: existing\nversion: 1.0.0\n";
        File.WriteAllText(path, original);

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "init", "--id", "replacement"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task AddManagedContent_WhenInputsValid_NormalizesAndPersistsSelectors()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();

        var fileExit = await workspace.Application.RunAsync(
            ["pack", "add", "file", @"docs\README.md"],
            workspace.Path
        );
        var directoryExit = await workspace.Application.RunAsync(
            ["pack", "add", "directory", @"templates\api"],
            workspace.Path
        );
        var globExit = await workspace.Application.RunAsync(
            ["pack", "add", "glob", @"guides\**\*.md"],
            workspace.Path
        );
        var manifest = await LoadAsync(workspace);

        await Assert
            .That(new[] { fileExit, directoryExit, globExit }.All(code => code == 0))
            .IsTrue();
        await Assert
            .That(manifest.ManagedFiles.Select(file => file.Source))
            .Contains("docs/README.md");
        await Assert
            .That(manifest.ManagedFiles.Select(file => file.Directory))
            .Contains("templates/api");
        await Assert
            .That(manifest.ManagedFiles.Select(file => file.Glob))
            .Contains("guides/**/*.md");
        await Assert
            .That(manifest.ManagedFiles.Single(file => file.Glob is not null).Target)
            .IsEqualTo("guides");
    }

    [Test]
    public async Task AddFile_WhenPathEscapes_PreservesManifest()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var original = File.ReadAllText(path);

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "add", "file", "../secret.txt"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task ScriptCommands_WhenCommandAndFileFormsAdded_PreserveLiteralArguments()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();

        var commandExit = await workspace.Application.RunAsync(
            ["pack", "add", "script", "command", "postInstall", "npm", "install"],
            workspace.Path
        );
        var fileExit = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "script",
                "file",
                "preInstall",
                @"scripts\setup.ps1",
                "pwsh",
                "-NoProfile",
            ],
            workspace.Path
        );
        var manifest = await LoadAsync(workspace);

        await Assert.That(commandExit).IsEqualTo(0);
        await Assert.That(fileExit).IsEqualTo(0);
        await Assert.That(manifest.Scripts!.PostInstall!.Command).IsEqualTo("npm");
        await Assert.That(manifest.Scripts.PostInstall.Arguments).IsEquivalentTo(["install"]);
        await Assert.That(manifest.Scripts.PreInstall!.File).IsEqualTo("scripts/setup.ps1");
        await Assert.That(manifest.Scripts.PreInstall.Runner).IsEqualTo("pwsh");
    }

    [Test]
    public async Task MetadataReferenceTagAndParameter_WhenAuthored_RoundTrip()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();

        var metadataExit = await workspace.Application.RunAsync(
            ["pack", "set", "homepage", "https://example.test/pack"],
            workspace.Path
        );
        var tagExit = await workspace.Application.RunAsync(
            ["pack", "add", "tag", "quality"],
            workspace.Path
        );
        var parameterExit = await workspace.Application.RunAsync(
            [
                "pack",
                "set",
                "parameter",
                "environment",
                "enum",
                "--value",
                "dev",
                "--value",
                "prod",
            ],
            workspace.Path
        );
        var referenceExit = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "reference",
                "foundation",
                "1.0.0",
                "--parameter",
                "enabled=true",
                "--disable-hook",
                "postInstall",
            ],
            workspace.Path
        );
        var manifest = await LoadAsync(workspace);

        await Assert
            .That(
                new[] { metadataExit, tagExit, parameterExit, referenceExit }.All(code => code == 0)
            )
            .IsTrue();
        await Assert.That(manifest.Homepage).IsEqualTo("https://example.test/pack");
        await Assert.That(manifest.Tags).Contains("quality");
        await Assert
            .That(manifest.Parameters["environment"].Values)
            .IsEquivalentTo(["dev", "prod"]);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, PackManifestStore.FileName)))
            .Contains("enabled: true");
    }

    [Test]
    public async Task RemoveCommands_WhenEntriesExist_RemoveOnlySelectedEntries()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        await workspace.Application.RunAsync(["pack", "add", "file", "README.md"], workspace.Path);
        await workspace.Application.RunAsync(
            ["pack", "add", "script", "command", "postInstall", "npm", "install"],
            workspace.Path
        );

        var fileExit = await workspace.Application.RunAsync(
            ["pack", "rm", "README.md"],
            workspace.Path
        );
        var scriptExit = await workspace.Application.RunAsync(
            ["pack", "rm", "script", "postInstall"],
            workspace.Path
        );
        var manifest = await LoadAsync(workspace);

        await Assert.That(fileExit).IsEqualTo(0);
        await Assert.That(scriptExit).IsEqualTo(0);
        await Assert.That(manifest.ManagedFiles).IsEmpty();
        await Assert.That(manifest.Scripts!.PostInstall).IsNull();
    }

    [Test]
    public async Task ShowAndValidate_WhenManifestValid_ReportLocalState()
    {
        var console = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: console);
        await workspace.Application.RunAsync(["pack", "init", "--id", "example"], workspace.Path);

        var showExit = await workspace.Application.RunAsync(["pack", "show"], workspace.Path);
        var validateExit = await workspace.Application.RunAsync(
            ["pack", "validate"],
            workspace.Path
        );

        await Assert.That(showExit).IsEqualTo(0);
        await Assert.That(validateExit).IsEqualTo(0);
        await Assert.That(console.Output).Contains("example");
        await Assert.That(console.Output).Contains("Manifest valid.");
    }

    private static async Task<TestWorkspace> CreateInitializedWorkspaceAsync()
    {
        var workspace = new TestWorkspace();
        var exitCode = await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example"],
            workspace.Path
        );
        if (exitCode != 0)
        {
            workspace.Dispose();
            throw new InvalidOperationException("Unable to initialize test pack.");
        }

        return workspace;
    }

    private static async Task<PackManifest> LoadAsync(TestWorkspace workspace)
    {
        var result = await new PackManifestStore(workspace.FileSystem).LoadAsync(workspace.Path);
        return result.Value ?? throw new InvalidOperationException(result.Error);
    }
}
