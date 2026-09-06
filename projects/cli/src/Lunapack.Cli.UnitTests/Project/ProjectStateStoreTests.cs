using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.UnitTests.Project;

public sealed class ProjectStateStoreTests
{
    private static readonly string[] _instanceNames = ["orders", "customers"];

    private const string VersionOneConfiguration =
        "schemaVersion: 1\nsources:\n  - name: git\n    type: git\n    url: https://example.test/packs.git\n    ref: main\npacks:\n  - id: root\n    version: 1.0.0\n    destination: docs/generated\n";

    private const string VersionOneLockFile =
        "schemaVersion: 1\npacks:\n  - id: root\n    version: 1.0.0\n    sourceName: git\n    sourceIdentity:\n      type: git\n      url: https://example.test/packs.git\n      ref: main\n    packPath: root\n    destination: docs/generated\n    gitSource:\n      type: git\n      url: https://example.test/packs.git\n      ref: main\n      resolvedCommit: 0123456789abcdef0123456789abcdef01234567\n    externalSources:\n      upstream:\n        sourceName: upstream-workspace\n        fingerprint: git:https://example.test/upstream.git@main\n        ref: main\n        resolvedCommit: fedcba9876543210fedcba9876543210fedcba98\n    packs: []\n    managedFiles:\n      - declaredTargetPath: templates/service.txt\n        targetPath: docs/generated/service.txt\n        sourceAlias: upstream\n        sourceName: upstream-workspace\n        sourceFingerprint: git:https://example.test/upstream.git@main\n        sourcePath: templates/service.txt\n        strategy:\n          type: merge\n          method: lines\n        sha256: 0000000000000000000000000000000000000000000000000000000000000000\n";

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
        await Assert.That(state.LockFile.SchemaVersion).IsEqualTo(2);
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
    public async Task Save_WhenPackInstancesNamed_RoundTripsIndependentEntries()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var state = CreateNamedInstanceState();

        var saved = await stateStore.SaveAsync(projectDirectory, state);
        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(saved.IsSuccess).IsTrue().Because(saved.Error ?? string.Empty);
        await Assert.That(loaded.IsSuccess).IsTrue().Because(loaded.Error ?? string.Empty);
        await Assert
            .That(
                loaded.RequireValue().Configuration.Packs.Select(pack => pack.Name).OfType<string>()
            )
            .IsEquivalentTo(_instanceNames);
        await Assert
            .That(loaded.RequireValue().LockFile.Instances.Select(instance => instance.Name))
            .IsEquivalentTo(_instanceNames);
    }

    [Test]
    public async Task Save_WhenPackInstancesOwnSameTarget_DoesNotCreateProjectDocuments()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var state = CreateNamedInstanceState();
        foreach (var instance in state.LockFile.Instances)
        {
            instance.ManagedFiles =
            [
                new ProjectLockFile.ManagedFile
                {
                    DeclaredTargetPath = "service.txt",
                    Sha256 = new string('0', 64),
                    TargetPath = "shared/service.txt",
                },
            ];
            instance.Placements["service.txt"] = "shared/service.txt";
        }

        var saved = await stateStore.SaveAsync(projectDirectory, state);

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert.That(saved.Error).Contains("multiple owners");
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
            .That(
                loaded.RequireValue().LockFile.Instances.Single().ManagedFiles.Single().TargetPath
            )
            .IsEqualTo("docs/generated/template.md");
        await Assert.That(configuration).DoesNotContain("\\");
        await Assert.That(lockFile).DoesNotContain("\\");
    }

    [Test]
    public async Task Load_WhenLockVersionOne_DerivesDefaultInstanceWithoutRewriting()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        var lockFilePath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.LockFileName
        );
        fileSystem.AddFile(configurationPath, new MockFileData(VersionOneConfiguration));
        fileSystem.AddFile(lockFilePath, new MockFileData(VersionOneLockFile));
        var stateStore = new ProjectStateStore(fileSystem);

        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(loaded.IsSuccess).IsTrue().Because(loaded.Error ?? string.Empty);
        var state = loaded.RequireValue();
        var instance = state.LockFile.Instances.Single();
        await Assert.That(state.LockFile.SchemaVersion).IsEqualTo(2);
        await Assert.That(instance.Name).IsEqualTo("root");
        await Assert.That(instance.Destination).IsEqualTo("docs/generated");
        await Assert
            .That(instance.Placements["templates/service.txt"])
            .IsEqualTo("docs/generated/service.txt");
        await Assert
            .That(instance.ExternalSources["upstream"].ResolvedCommit)
            .IsEqualTo("fedcba9876543210fedcba9876543210fedcba98");
        await Assert.That(instance.ManagedFiles.Single().Strategy?.Method).IsEqualTo("lines");
        await Assert
            .That(state.LockFile.Packs.Single().Key?.ResolvedCommit)
            .IsEqualTo("0123456789abcdef0123456789abcdef01234567");
        await Assert
            .That(fileSystem.File.ReadAllText(configurationPath))
            .IsEqualTo(VersionOneConfiguration);
        await Assert.That(fileSystem.File.ReadAllText(lockFilePath)).IsEqualTo(VersionOneLockFile);
    }

    [Test]
    public async Task Save_WhenVersionOneLoaded_WritesVersionTwoAndPreservesEvidence()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(VersionOneConfiguration)
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData(VersionOneLockFile)
        );
        var stateStore = new ProjectStateStore(fileSystem);
        var loaded = await stateStore.LoadAsync(projectDirectory);

        var saved = await stateStore.SaveAsync(projectDirectory, loaded.RequireValue());
        var reloaded = await stateStore.LoadAsync(projectDirectory);
        var persistedLock = fileSystem.File.ReadAllText(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName)
        );

        await Assert.That(saved.IsSuccess).IsTrue().Because(saved.Error ?? string.Empty);
        await Assert.That(persistedLock).Contains("schemaVersion: 2");
        await Assert.That(persistedLock).Contains("instances:");
        await Assert.That(persistedLock).Contains("rootResolution:");
        var instance = reloaded.RequireValue().LockFile.Instances.Single();
        await Assert.That(instance.ManagedFiles.Single().Strategy?.Method).IsEqualTo("lines");
        await Assert
            .That(instance.ManagedFiles.Single().SourceFingerprint)
            .IsEqualTo("git:https://example.test/upstream.git@main");
    }

    [Test]
    public async Task Load_WhenVersionOneCorrelationAmbiguous_FailsWithoutMutation()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        const string configuration = """
            schemaVersion: 1
            sources:
              - name: local
                type: local
                path: packs
            packs:
              - id: root
                name: orders
              - id: root
                name: customers

            """;
        const string lockFile = """
            schemaVersion: 1
            packs:
              - id: root
                version: 1.0.0
                sourceName: local
                sourceIdentity:
                  type: local
                  path: packs
                sourcePath: packs
                packPath: root
                packs: []
                managedFiles: []

            """;
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        var lockFilePath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.LockFileName
        );
        fileSystem.AddFile(configurationPath, new MockFileData(configuration));
        fileSystem.AddFile(lockFilePath, new MockFileData(lockFile));
        var stateStore = new ProjectStateStore(fileSystem);

        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(loaded.IsSuccess).IsFalse();
        await Assert.That(loaded.Error).Contains("cannot unambiguously correlate");
        await Assert.That(fileSystem.File.ReadAllText(configurationPath)).IsEqualTo(configuration);
        await Assert.That(fileSystem.File.ReadAllText(lockFilePath)).IsEqualTo(lockFile);
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
    public async Task Load_WhenUnknownOptionalPropertiesDeclared_IgnoresValuesAndUsesDefaults()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(
                "schemaVersion: 1\nsources:\n  - name: local\n    type: local\n    path: source\n    futureOptions:\n      enabled: true\npacks: []\nfutureValues: [one, two]\n"
            )
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData("schemaVersion: 1\npacks: []\nfutureOptions:\n  enabled: true\n")
        );
        var stateStore = new ProjectStateStore(fileSystem);

        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(loaded.IsSuccess).IsTrue().Because(loaded.Error ?? string.Empty);
        var state = loaded.RequireValue();
        await Assert.That(state.Configuration.Variables).IsEmpty();
        await Assert.That(state.LockFile.Links).IsEmpty();
    }

    [Test]
    [Arguments(
        "schemaVersion: 1\nsources:\n  - name: local\n    name: replacement\n    type: local\n    path: source\npacks: []\n"
    )]
    [Arguments(
        "schemaVersion: 1\nsources:\n  - name: git\n    type: git\n    url: https://example.test/packs.git\n    timeoutSeconds: invalid\npacks: []\n"
    )]
    [Arguments(
        "schemaVersion: 1\nsources: []\npacks: []\nvariables:\n  mode: first\n  mode: second\n"
    )]
    public async Task Load_WhenConfigurationYamlIsAmbiguousOrMalformed_ReturnsFailure(
        string configuration
    )
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(configuration)
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData("schemaVersion: 1\npacks: []\n")
        );
        var stateStore = new ProjectStateStore(fileSystem);

        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(loaded.IsSuccess).IsFalse();
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

    private static ProjectState CreateNamedInstanceState()
    {
        var key = new ProjectLockFile.ResolvedPackKey
        {
            Id = "dotnet-api",
            SourceIdentity = ConfiguredSourceIdentity.CreateLocal("packs"),
            Version = "1.0.0",
        };
        return new ProjectState
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Sources = [new ProjectConfiguration.LocalSource { Name = "local", Path = "packs" }],
                Packs =
                [
                    new ProjectConfiguration.RequestedPack
                    {
                        Id = "dotnet-api",
                        Name = "orders",
                        Destination = "src/OrdersApi",
                    },
                    new ProjectConfiguration.RequestedPack
                    {
                        Id = "dotnet-api",
                        Name = "customers",
                        Destination = "src/CustomersApi",
                    },
                ],
            },
            LockFile = new ProjectLockFile
            {
                SchemaVersion = 2,
                Instances =
                [
                    new ProjectLockFile.PackInstance
                    {
                        Destination = "src/OrdersApi",
                        Id = "dotnet-api",
                        Name = "orders",
                        RootResolution = key,
                    },
                    new ProjectLockFile.PackInstance
                    {
                        Destination = "src/CustomersApi",
                        Id = "dotnet-api",
                        Name = "customers",
                        RootResolution = key,
                    },
                ],
                Packs =
                [
                    new ProjectLockFile.ResolvedPack
                    {
                        Id = "dotnet-api",
                        Key = key,
                        PackPath = "dotnet-api",
                        SourceIdentity = ConfiguredSourceIdentity.CreateLocal("packs"),
                        SourceName = "local",
                        SourcePath = "packs",
                        Version = "1.0.0",
                    },
                ],
            },
        };
    }

    private static MockFileSystem CreateFileSystem() => new();
}
