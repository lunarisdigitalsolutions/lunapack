using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.UnitTests.Project;

public sealed class ProjectStateStoreTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LoadAndSave_WhenProjectScriptDenialConfigured_PreservesValue(bool scripts)
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(
                $"schemaVersion: 1\nsources: []\npacks: []\ntrust:\n  deny:\n    scripts: {scripts.ToString().ToLowerInvariant()}\n"
            )
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData("schemaVersion: 1\npacks: []\n")
        );
        var stateStore = new ProjectStateStore(fileSystem);

        var loaded = await stateStore.LoadAsync(projectDirectory);
        var saved = await stateStore.SaveAsync(projectDirectory, loaded.RequireValue());
        var reloaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert
            .That(reloaded.RequireValue().Configuration.Trust.Deny?.Scripts)
            .IsEqualTo(scripts);
    }

    [Test]
    public async Task Save_WhenStateValid_PersistsConfigurationAndLockFile()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);

        var saved = await stateStore.SaveAsync(projectDirectory, CreateValidState());
        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert.That(loaded.IsSuccess).IsTrue();
        var state = loaded.RequireValue();
        await Assert.That(state.Configuration.SchemaVersion).IsEqualTo(1);
        await Assert.That(state.Configuration.Trust.Sources).IsEmpty();
        await Assert.That(state.Configuration.Trust.Packs).IsEmpty();
        await Assert.That(state.Configuration.Variables).IsEmpty();
        await Assert.That(state.LockFile.SchemaVersion).IsEqualTo(1);
        var configuration = fileSystem.File.ReadAllText(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName)
        );
        await Assert.That(configuration).Contains("trust:");
        await Assert.That(configuration).Contains("variables: {}");
    }

    [Test]
    public async Task Save_WhenConfigurationContainsManagedTargetRemapping_PersistsRemapping()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var state = CreateValidState() with
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Remap = new ProjectConfiguration.Remapping
                {
                    Directories = { ["docs/adr"] = "docs/internal/01-architecture/decisions" },
                    Files = { ["docs/adr/template.md"] = "docs/adr/_template.md" },
                },
            },
        };

        var saved = await stateStore.SaveAsync(projectDirectory, state);
        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(saved.IsSuccess).IsTrue();
        var remapping = loaded.RequireValue().Configuration.Remap;
        await Assert.That(remapping).IsNotNull();
        await Assert
            .That(remapping.RequireNotNull().Directories["docs/adr"])
            .IsEqualTo("docs/internal/01-architecture/decisions");
        await Assert
            .That(remapping.Files["docs/adr/template.md"])
            .IsEqualTo("docs/adr/_template.md");
    }

    [Test]
    public async Task Save_WhenVariableIsStringArray_RoundTripsOrderedValues()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var state = CreateValidState();
        state.Configuration.Variables["features"] = new List<string> { "docker", "api" };

        var saved = await stateStore.SaveAsync(projectDirectory, state);
        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(saved.IsSuccess).IsTrue().Because(saved.Error ?? string.Empty);
        await Assert.That(loaded.IsSuccess).IsTrue().Because(loaded.Error ?? string.Empty);
        await Assert
            .That(loaded.RequireValue().Configuration.Variables["features"])
            .IsEquivalentTo(new List<string> { "docker", "api" });
    }

    [Test]
    public async Task LoadAndSave_WhenDocumentsContainWindowsPaths_UsesCanonicalPaths()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(
                "schemaVersion: 1\nsources:\n  - name: local\n    type: local\n    path: 'packs\\catalog'\npacks:\n  - id: example\n    version: 1.0.0\n    destination: 'docs\\generated'\ntrust:\n  sources: []\n  packs: []\nremap:\n  directories:\n    'docs\\adr': 'docs\\architecture\\adr'\n  files:\n    'docs\\adr\\template.md': 'docs\\adr\\_template.md'\n"
            )
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData(
                "schemaVersion: 1\npacks:\n  - id: example\n    version: 1.0.0\n    sourcePath: 'packs\\catalog'\n    sourceName: local\n    sourceIdentity:\n      type: local\n      path: 'packs\\catalog'\n    packPath: 'templates\\example'\n    destination: 'docs\\generated'\n    packs: []\n    managedFiles:\n      - declaredTargetPath: 'docs\\adr\\template.md'\n        targetPath: 'docs\\generated\\template.md'\n        sha256: 0000000000000000000000000000000000000000000000000000000000000000\n"
            )
        );
        var stateStore = new ProjectStateStore(fileSystem);

        var loaded = await stateStore.LoadAsync(projectDirectory);
        var saved = await stateStore.SaveAsync(projectDirectory, loaded.RequireValue());
        var configuration = fileSystem.File.ReadAllText(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName)
        );
        var lockFile = fileSystem.File.ReadAllText(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName)
        );

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert
            .That(loaded.RequireValue().Configuration.Packs.Single().Destination)
            .IsEqualTo("docs/generated");
        await Assert
            .That(
                loaded.RequireValue().Configuration.Remap.RequireNotNull().Directories["docs/adr"]
            )
            .IsEqualTo("docs/architecture/adr");
        await Assert
            .That(loaded.RequireValue().LockFile.Packs.Single().ManagedFiles.Single().TargetPath)
            .IsEqualTo("docs/generated/template.md");
        await Assert.That(configuration).DoesNotContain("\\");
        await Assert.That(lockFile).DoesNotContain("\\");
    }

    [Test]
    public async Task Save_WhenConfigurationInvalid_DoesNotCreateProjectDocuments()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var invalidState = CreateValidState() with
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 2,
                Sources =
                [
                    new ProjectConfiguration.LocalSource { Name = "local", Path = @"C:\packs" },
                ],
            },
        };

        var saved = await stateStore.SaveAsync(projectDirectory, invalidState);

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert
            .That(
                fileSystem.File.Exists(
                    fileSystem.Path.Combine(
                        projectDirectory,
                        ProjectStateStore.ConfigurationFileName
                    )
                )
            )
            .IsFalse();
        await Assert
            .That(
                fileSystem.File.Exists(
                    fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName)
                )
            )
            .IsFalse();
    }

    [Test]
    public async Task Save_WhenStateContainsGitSource_PersistsTypedGitSource()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var state = CreateValidState() with
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Sources =
                [
                    new ProjectConfiguration.GitSource
                    {
                        Name = "git",
                        Url = "https://example.test/packs.git",
                        Ref = "main",
                        Path = "packs",
                        TimeoutSeconds = 120,
                    },
                ],
            },
        };

        var saved = await stateStore.SaveAsync(projectDirectory, state);
        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(saved.IsSuccess).IsTrue();
        var source = loaded.RequireValue().Configuration.Sources.Single();
        await Assert.That(source).IsTypeOf<ProjectConfiguration.GitSource>();
        var gitSource = (ProjectConfiguration.GitSource)source;
        await Assert.That(gitSource.Url).IsEqualTo("https://example.test/packs.git");
        await Assert.That(gitSource.Ref).IsEqualTo("main");
        await Assert.That(gitSource.Path).IsEqualTo("packs");
        await Assert.That(gitSource.TimeoutSeconds).IsEqualTo(120);
    }

    [Test]
    public async Task Load_WhenLockContainsUnreachablePack_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(
                "schemaVersion: 1\nsources:\n  - name: local\n    type: local\n    path: source\npacks:\n  - id: root\n    version: 1.0.0\ntrust:\n  sources: []\n  packs: []\n"
            )
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData(
                "schemaVersion: 1\npacks:\n  - id: root\n    version: 1.0.0\n    sourcePath: source\n    sourceName: local\n    sourceIdentity:\n      type: local\n      path: source\n    packPath: root\n    packs: []\n    managedFiles: []\n  - id: injected\n    version: 1.0.0\n    sourcePath: source\n    sourceName: local\n    sourceIdentity:\n      type: local\n      path: source\n    packPath: injected\n    packs: []\n    managedFiles: []\n"
            )
        );
        var stateStore = new ProjectStateStore(fileSystem);

        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(loaded.IsSuccess).IsFalse();
    }

    [Test]
    [Arguments("other", "source")]
    [Arguments("local", "other-source")]
    public async Task Save_WhenLockSourceDoesNotMatch_ReturnsFailure(
        string sourceName,
        string sourcePath
    )
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);

        var saved = await stateStore.SaveAsync(
            projectDirectory,
            CreateStateWithSourceIdentity(sourceName, sourcePath)
        );

        await Assert.That(saved.IsSuccess).IsFalse();
    }

    private static ProjectState CreateStateWithSourceIdentity(
        string sourceName,
        string sourcePath
    ) =>
        new()
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Sources =
                [
                    new ProjectConfiguration.LocalSource { Name = "local", Path = "source" },
                ],
                Packs = [new ProjectConfiguration.RequestedPack { Id = "root", Version = "1.0.0" }],
            },
            LockFile = new ProjectLockFile
            {
                SchemaVersion = 1,
                Packs =
                [
                    new ProjectLockFile.ResolvedPack
                    {
                        Id = "root",
                        Version = "1.0.0",
                        SourceName = sourceName,
                        SourceIdentity = ConfiguredSourceIdentity.CreateLocal(sourcePath),
                        SourcePath = sourcePath,
                        PackPath = "root",
                    },
                ],
            },
        };

    private static ProjectState CreateValidState() =>
        new()
        {
            Configuration = new ProjectConfiguration { SchemaVersion = 1 },
            LockFile = new ProjectLockFile { SchemaVersion = 1 },
        };

    private static MockFileSystem CreateFileSystem() => new();
}
