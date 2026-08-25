using System.IO.Abstractions.TestingHelpers;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests;

public sealed class CliApplicationTests
{
    [Test]
    public async Task Install_WhenRequiredParameterUnresolved_PromptsAndInstalls()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Input.PushTextWithEnter("Prompted Corporation");
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var packDirectory = Path.Combine(workspace.Path, "source", "license-mit");
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templatesDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            "id: license-mit\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nparameters:\n  companyName:\n    type: string\n    required: true\n    displayName: Company name\n    description: Legal entity name.\nmanagedFiles:\n  - source: templates/content.txt\n    target: LICENSE.md\n    template: true\n"
        );
        File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), "{{ companyName }}");
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["install", "license-mit"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("Company name");
        await Assert.That(ansiConsole.Output).Contains("Legal entity name.");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "LICENSE.md")))
            .IsEqualTo("Prompted Corporation");
    }

    [Test]
    public async Task Install_WhenPackHasNoParameters_PersistsRequestedRoot()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var packDirectory = Path.Combine(workspace.Path, "source", "example");
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templatesDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            "id: example\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: example.txt\n"
        );
        File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), "example");
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["install", "example"], workspace.Path);
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).DoesNotContain("error:");
        await Assert
            .That(state.RequireValue().Configuration.Packs.Single().Id)
            .IsEqualTo("example");
        await Assert.That(state.RequireValue().LockFile.Packs.Single().Id).IsEqualTo("example");
    }

    [Test]
    public async Task Install_WhenExistingRootHasRequiredParameter_DoesNotPromptForNewUnrelatedRoot()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        CreatePack(
            workspace.Path,
            "license",
            "id: license\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: LICENSE.md\n    template: true\n",
            "{{ companyName }}"
        );
        CreatePack(
            workspace.Path,
            "sdk",
            "id: sdk\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: sdk.txt\n",
            "sdk"
        );
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["install", "license", "--parameter", "companyName=Lunaris"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["install", "sdk"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).DoesNotContain("Company name");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "sdk.txt")))
            .IsEqualTo("sdk");
    }

    [Test]
    public async Task Install_WhenSourceContainsUnrelatedInvalidPack_DoesNotWriteWarning()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        CreatePack(
            workspace.Path,
            "example",
            "id: example\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: example.txt\n",
            "example"
        );
        var invalidPackDirectory = Path.Combine(workspace.Path, "source", "invalid");
        Directory.CreateDirectory(invalidPackDirectory);
        File.WriteAllText(
            Path.Combine(invalidPackDirectory, "pack.yml"),
            "id: invalid\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/missing.txt\n    target: missing.txt\n"
        );
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["install", "example"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).DoesNotContain("warning:");
        await Assert.That(ansiConsole.Output).DoesNotContain("invalid");
    }

    [Test]
    public async Task Init_WhenManifestMissing_CreatesSchemaValidManifest()
    {
        using var workspace = new TestWorkspace();

        var exitCode = await workspace.Application.RunAsync(["init"], workspace.Path);
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(state.IsSuccess).IsTrue();
        var projectState = state.RequireValue();
        await Assert.That(projectState.Configuration.SchemaVersion).IsEqualTo(1);
        await Assert.That(projectState.Configuration.Sources).IsEmpty();
        await Assert.That(projectState.Configuration.Packs).IsEmpty();
        await Assert.That(projectState.LockFile.SchemaVersion).IsEqualTo(1);
        await Assert.That(projectState.LockFile.Packs).IsEmpty();
    }

    [Test]
    public async Task Init_WhenManifestExists_PreservesExistingContent()
    {
        using var workspace = new TestWorkspace();
        var manifestPath = Path.Combine(workspace.Path, ProjectManifestStore.FileName);
        const string existingManifest =
            "schemaVersion: 1\nsources: []\npacks: []\ntrust:\n  sources: []\n  packs: []\n";
        File.WriteAllText(manifestPath, existingManifest);

        var exitCode = await workspace.Application.RunAsync(["init"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(existingManifest);
    }

    [Test]
    public async Task Init_WhenLockFileExists_PreservesExistingContent()
    {
        using var workspace = new TestWorkspace();
        var lockFilePath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        const string existingLockFile = "schemaVersion: 1\npacks: []\n";
        File.WriteAllText(lockFilePath, existingLockFile);

        var exitCode = await workspace.Application.RunAsync(["init"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(lockFilePath)).IsEqualTo(existingLockFile);
        await Assert
            .That(
                File.Exists(Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName))
            )
            .IsFalse();
    }

    [Test]
    public async Task Inspect_WhenGlobalRemappingMatches_DisplaysEffectiveManagedTarget()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        CreatePack(
            workspace.Path,
            "inspectable",
            "id: inspectable\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: docs/adr/template.md\n",
            "content"
        );
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        state.Configuration.Remap = new ProjectConfiguration.Remapping
        {
            Directories = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["docs/adr"] = "docs/architecture/adr",
            },
        };
        await workspace.StateStore.SaveAsync(workspace.Path, state);

        var exitCode = await workspace.Application.RunAsync(
            ["inspect", "inspectable"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0).Because(ansiConsole.Output);
        await Assert
            .That(ansiConsole.Output)
            .Contains("docs/adr/template.md -> docs/architecture/adr/template.md");
        await Assert.That(ansiConsole.Output).DoesNotContain("templates/template.md");
    }

    [Test]
    public async Task Command_WhenArgumentsUnsupported_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();

        var exitCode = await workspace.Application.RunAsync(
            ["sources", "add", "local"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
    }

    [Test]
    public async Task Help_WhenRequested_ReturnsSuccess()
    {
        using var workspace = new TestWorkspace();

        var exitCode = await workspace.Application.RunAsync(["--help"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task Variables_SetThenList_PersistsAndRendersVariable()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await workspace.Application.RunAsync(["init"], workspace.Path);

        var setExitCode = await workspace.Application.RunAsync(
            ["variables", "set", "companyName", "Lunaris"],
            workspace.Path
        );
        var listExitCode = await workspace.Application.RunAsync(
            ["variables", "list"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(setExitCode).IsEqualTo(0);
        await Assert.That(listExitCode).IsEqualTo(0);
        await Assert
            .That(state.RequireValue().Configuration.Variables["companyName"])
            .IsEqualTo("Lunaris");
        await Assert.That(ansiConsole.Output).Contains("Project variables");
        await Assert.That(ansiConsole.Output).Contains("companyName");
        await Assert.That(ansiConsole.Output).Contains("Lunaris");
    }

    [Test]
    public async Task Variables_Remove_WhenVariableConfigured_RemovesVariable()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["variables", "set", "companyName", "Lunaris"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["variables", "rm", "companyName"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(state.RequireValue().Configuration.Variables)
            .DoesNotContainKey("companyName");
    }

    [Test]
    public async Task Remap_WhenDirectoryProvided_PersistsNormalizedMapping()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["remap", "set", "directory", "docs\\adr", "docs/internal/decisions/"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(state.RequireValue().Configuration.Remap!.Directories["docs/adr"])
            .IsEqualTo("docs/internal/decisions");
        await Assert
            .That(
                File.ReadAllText(
                    Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName)
                )
            )
            .DoesNotContain("\\");
    }

    [Test]
    public async Task Remap_WhenFileProvided_PreservesExistingDirectoryMapping()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["remap", "set", "directory", "docs/adr", "docs/internal/decisions"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["remap", "set", "file", "docs/adr/template.md", "docs/adr/_template.md"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);
        var remapping = state.RequireValue().Configuration.Remap!;

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(remapping.Directories["docs/adr"]).IsEqualTo("docs/internal/decisions");
        await Assert
            .That(remapping.Files["docs/adr/template.md"])
            .IsEqualTo("docs/adr/_template.md");
    }

    [Test]
    public async Task Remap_ListAndRemove_WhenMappingConfigured_RendersThenRemovesMapping()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["remap", "set", "directory", "docs/adr", "docs/architecture/adr"],
            workspace.Path
        );

        var listExitCode = await workspace.Application.RunAsync(["remap", "list"], workspace.Path);
        var removeExitCode = await workspace.Application.RunAsync(
            ["remap", "rm", "directory", ".\\docs\\adr"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(listExitCode).IsEqualTo(0);
        await Assert.That(removeExitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("Managed target remappings");
        await Assert.That(ansiConsole.Output).Contains("docs/adr");
        await Assert.That(state.RequireValue().Configuration.Remap).IsNull();
    }

    [Test]
    public async Task Remap_WhenTargetAlreadyMapped_ReplacesDestination()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["remap", "set", "directory", "docs/adr", "docs/internal/decisions"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["remap", "set", "directory", "docs/adr", "docs/architecture/adr"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);
        var mappings = state.RequireValue().Configuration.Remap!.Directories;

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(mappings).Count().IsEqualTo(1);
        await Assert.That(mappings["docs/adr"]).IsEqualTo("docs/architecture/adr");
    }

    [Test]
    public async Task Remap_WhenKindOrPathInvalid_LeavesConfigurationUnchanged()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var initialConfiguration = File.ReadAllText(configurationPath);

        var invalidKindExitCode = await workspace.Application.RunAsync(
            ["remap", "set", "folder", "docs/adr", "docs/internal/decisions"],
            workspace.Path
        );
        var escapingPathExitCode = await workspace.Application.RunAsync(
            ["remap", "set", "directory", "docs/adr", "../outside"],
            workspace.Path
        );

        await Assert.That(invalidKindExitCode).IsEqualTo(1);
        await Assert.That(escapingPathExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
    }

    [Test]
    public async Task Discover_WhenVersionsOmitted_RendersLatestReleaseInVersionColumn()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await ConfigureVersionedPackCatalogAsync(workspace);

        var exitCode = await workspace.Application.RunAsync(["discover"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("Version");
        await Assert.That(ansiConsole.Output).Contains("2.0.0");
        await Assert.That(ansiConsole.Output).DoesNotContain("1.0.0");
    }

    [Test]
    public async Task Discover_WhenVersionsSpecified_RendersRequestedRecentReleases()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await ConfigureVersionedPackCatalogAsync(workspace);

        var exitCode = await workspace.Application.RunAsync(
            ["discover", "--versions", "2"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("2.0.0");
        await Assert.That(ansiConsole.Output).Contains("1.0.0");
    }

    [Test]
    public async Task Search_WhenVersionsOmitted_RendersLatestRelease()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await ConfigureVersionedPackCatalogAsync(workspace);

        var exitCode = await workspace.Application.RunAsync(["search", "example"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("2.0.0");
        await Assert.That(ansiConsole.Output).DoesNotContain("1.0.0");
    }

    [Test]
    public async Task Search_WhenVersionsSpecified_RendersRequestedRecentReleases()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await ConfigureVersionedPackCatalogAsync(workspace);

        var exitCode = await workspace.Application.RunAsync(
            ["search", "example", "--versions", "2"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("2.0.0");
        await Assert.That(ansiConsole.Output).Contains("1.0.0");
    }

    [Test]
    public async Task CatalogCommands_WhenVersionLimitExceedsTen_ReturnFailure()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await ConfigureVersionedPackCatalogAsync(workspace);

        var discoverExitCode = await workspace.Application.RunAsync(
            ["discover", "--versions", "11"],
            workspace.Path
        );
        var searchExitCode = await workspace.Application.RunAsync(
            ["search", "example", "--versions", "11"],
            workspace.Path
        );

        await Assert.That(discoverExitCode).IsEqualTo(1);
        await Assert.That(searchExitCode).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).Contains("between 1 and 10");
    }

    [Test]
    public async Task PackCommandHelp_ReturnsSuccess()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);

        foreach (var command in new[] { "install", "validate", "inspect", "uninstall", "update" })
        {
            var exitCode = await workspace.Application.RunAsync(
                [command, "--help"],
                workspace.Path
            );

            await Assert.That(exitCode).IsEqualTo(0);
        }
    }

    [Test]
    public async Task SourceAddLocal_WhenDirectoryExists_RecordsSuppliedPath()
    {
        using var workspace = new TestWorkspace();
        const string sourcePath = "source";
        Directory.CreateDirectory(Path.Combine(workspace.Path, sourcePath));
        await workspace.Application.RunAsync(["init"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", sourcePath],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        var projectState = state.RequireValue();
        await Assert.That(projectState.Configuration.Sources).Count().IsEqualTo(1);
        await Assert
            .That(projectState.Configuration.Sources[0])
            .IsTypeOf<ProjectConfiguration.LocalSource>();
        var configuredSource = (ProjectConfiguration.LocalSource)
            projectState.Configuration.Sources[0];
        await Assert.That(configuredSource.Name).IsEqualTo("local");
        await Assert.That(configuredSource.Path).IsEqualTo(sourcePath);
        await Assert.That(configuredSource.Type).IsEqualTo("local");
    }

    private static void CreatePack(
        string projectDirectory,
        string id,
        string manifest,
        string contents
    )
    {
        var templatesDirectory = Path.Combine(projectDirectory, "source", id, "templates");
        Directory.CreateDirectory(templatesDirectory);
        File.WriteAllText(Path.Combine(templatesDirectory, "..", "pack.yml"), manifest);
        File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), contents);
    }

    private static async Task ConfigureVersionedPackCatalogAsync(TestWorkspace workspace)
    {
        CreatePack(
            workspace.Path,
            "example-v1",
            "id: example\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: example.txt\n",
            "example"
        );
        CreatePack(
            workspace.Path,
            "example-v2",
            "id: example\nversion: 2.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: example.txt\n",
            "example"
        );
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
    }

    [Test]
    public async Task SourceAddGit_WhenValid_RecordsConfiguredValues()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init", "-w", workspace.Path], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            [
                "sources",
                "add",
                "git",
                "git",
                "https://example.test/packs.git",
                "-r",
                "main",
                "-p",
                "packs/platform",
            ],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        var source = state.RequireValue().Configuration.Sources.Single();
        await Assert.That(source).IsTypeOf<ProjectConfiguration.GitSource>();
        var gitSource = (ProjectConfiguration.GitSource)source;
        await Assert.That(gitSource.Name).IsEqualTo("git");
        await Assert.That(gitSource.Url).IsEqualTo("https://example.test/packs.git");
        await Assert.That(gitSource.Ref).IsEqualTo("main");
        await Assert.That(gitSource.Path).IsEqualTo("packs/platform");
    }

    [Test]
    public async Task SourceAddGitHub_WhenValid_RecordsEquivalentGitSource()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            [
                "sources",
                "add",
                "github",
                "github",
                "acme/engineering-packs",
                "--ref",
                "main",
                "--path",
                "packs/platform",
            ],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        var source = state.RequireValue().Configuration.Sources.Single();
        await Assert.That(source).IsTypeOf<ProjectConfiguration.GitSource>();
        var gitSource = (ProjectConfiguration.GitSource)source;
        await Assert.That(gitSource.Name).IsEqualTo("github");
        await Assert.That(gitSource.Url).IsEqualTo("https://github.com/acme/engineering-packs.git");
        await Assert.That(gitSource.Ref).IsEqualTo("main");
        await Assert.That(gitSource.Path).IsEqualTo("packs/platform");
    }

    [Test]
    public async Task SourceAddGitHub_WhenRepositoryIsMalformed_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var initialConfiguration = File.ReadAllText(configurationPath);

        var exitCode = await workspace.Application.RunAsync(
            ["sources", "add", "github", "github", "acme/engineering/packs"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
    }

    [Test]
    public async Task SourceAddGit_WhenDuplicate_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var sourceArguments = new[]
        {
            "sources",
            "add",
            "git",
            "git",
            "https://example.test/packs.git",
            "--ref",
            "main",
        };
        await workspace.Application.RunAsync(sourceArguments, workspace.Path);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var initialConfiguration = File.ReadAllText(configurationPath);

        var exitCode = await workspace.Application.RunAsync(sourceArguments, workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
    }

    [Test]
    public async Task SourceAddGit_WhenNameUsedByLocalSource_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "shared", "source"],
            workspace.Path
        );
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var initialConfiguration = File.ReadAllText(configurationPath);

        var exitCode = await workspace.Application.RunAsync(
            ["sources", "add", "git", "shared", "https://example.test/packs.git"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
    }

    [Test]
    public async Task SourceAddGit_WhenPathEscapesRepository_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var initialConfiguration = File.ReadAllText(configurationPath);

        var exitCode = await workspace.Application.RunAsync(
            [
                "sources",
                "add",
                "git",
                "git",
                "https://example.test/packs.git",
                "--path",
                "../packs",
            ],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
    }

    [Test]
    public async Task SourceAddLocal_WhenManifestMissing_LeavesProjectUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = Directory.CreateDirectory(Path.Combine(workspace.Path, "source")).FullName;

        var exitCode = await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", sourcePath],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, ProjectManifestStore.FileName)))
            .IsFalse();
    }

    [Test]
    public async Task SourceAddLocal_WhenDirectoryMissing_LeavesManifestUnchanged()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var manifestPath = Path.Combine(workspace.Path, ProjectManifestStore.FileName);
        var initialManifest = File.ReadAllText(manifestPath);
        const string unavailablePath = "missing";

        var exitCode = await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", unavailablePath],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(initialManifest);
    }

    [Test]
    public async Task SourceAddLocal_WhenPathDuplicated_LeavesManifestUnchanged()
    {
        using var workspace = new TestWorkspace();
        const string sourcePath = "source";
        Directory.CreateDirectory(Path.Combine(workspace.Path, sourcePath));
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "first", sourcePath],
            workspace.Path
        );
        var manifestPath = Path.Combine(workspace.Path, ProjectManifestStore.FileName);
        var initialManifest = File.ReadAllText(manifestPath);

        var exitCode = await workspace.Application.RunAsync(
            ["sources", "add", "local", "second", sourcePath],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(initialManifest);
    }

    [Test]
    public async Task SourceAddLocal_WhenManifestInvalid_LeavesManifestUnchanged()
    {
        using var workspace = new TestWorkspace();
        const string sourcePath = "source";
        Directory.CreateDirectory(Path.Combine(workspace.Path, sourcePath));
        var manifestPath = Path.Combine(workspace.Path, ProjectManifestStore.FileName);
        const string invalidManifest = "schemaVersion: 3\nsources: []\npacks: []\n";
        File.WriteAllText(manifestPath, invalidManifest);
        File.WriteAllText(
            Path.Combine(workspace.Path, ProjectStateStore.LockFileName),
            "schemaVersion: 1\npacks: []\n"
        );

        var exitCode = await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", sourcePath],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(invalidManifest);
    }

    [Test]
    public async Task SourceAddLocal_WhenPathAbsolute_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var lockFilePath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var initialConfiguration = File.ReadAllText(configurationPath);
        var initialLockFile = File.ReadAllText(lockFilePath);

        var exitCode = await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", workspace.Path],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
        await Assert.That(File.ReadAllText(lockFilePath)).IsEqualTo(initialLockFile);
    }

    [Test]
    public async Task SourceAddLocal_WhenMockFilesystemDirectoryMissing_ReturnsFailure()
    {
        var fileSystem = new MockFileSystem();
        var application = new CliApplication(fileSystem, TestConsole.CreateAnsiConsole());

        var exitCode = await application.RunAsync(
            ["sources", "add", "local", "local", @"C:\packs"],
            @"C:\project"
        );

        await Assert.That(exitCode).IsEqualTo(1);
    }
}
