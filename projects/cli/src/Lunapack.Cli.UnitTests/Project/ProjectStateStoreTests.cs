using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;

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
    public async Task Load_WhenProjectConfigurationExceedsBootstrapCeiling_RejectsBeforeDeserialization()
    {
        using var workspace = new TestWorkspace();
        var savedSettings = await workspace.UserSettingsStore.SaveAsync(
            new UserSettings
            {
                Global = new UserTrust
                {
                    Preferences = new CliPreferences
                    {
                        ResourceLimits = new ResourceLimitPreferences
                        {
                            ProjectManifestBytes = 16_777_216,
                        },
                    },
                },
            }
        );
        File.WriteAllText(
            Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName),
            new string('x', 16_777_217)
        );

        var loaded = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(savedSettings.IsSuccess).IsTrue();
        await Assert.That(loaded.IsSuccess).IsFalse();
        await Assert.That(loaded.Error).Contains("at least 16777217 bytes");
    }

    [Test]
    public async Task Load_WhenProjectResourceLimitKeyIsUnknown_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(
                "schemaVersion: 1\npreferences:\n  resourceLimits:\n    packManifestBytez: 1024\n"
            )
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData("schemaVersion: 1\npacks: []\n")
        );

        var loaded = await new ProjectStateStore(fileSystem).LoadAsync(projectDirectory);

        await Assert.That(loaded.IsSuccess).IsFalse();
        await Assert.That(loaded.Error).Contains("Unknown resource-limit key 'packManifestBytez'");
    }

    [Test]
    public async Task Load_WhenLockCollectionExceedsEffectiveLimit_FailsDuringYamlPreflight()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(
                "schemaVersion: 1\nsources: []\npacks: []\npreferences:\n  resourceLimits:\n    dependencyNodes: 1\n"
            )
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData("schemaVersion: 1\npacks:\n  - {}\n  - {}\n")
        );

        var loaded = await new ProjectStateStore(fileSystem).LoadAsync(projectDirectory);

        await Assert.That(loaded.IsSuccess).IsFalse();
        await Assert
            .That(loaded.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(loaded.Error).Contains("2 pack records");
    }

    [Test]
    public async Task Load_WhenLockUsesAlias_FailsDuringYamlPreflight()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData("schemaVersion: 1\nsources: []\npacks: []\n")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData(
                "schemaVersion: 1\nshared: &pack\n  id: alias\n  version: 1.0.0\n  packPath: .\npacks:\n  - *pack\n"
            )
        );

        var loaded = await new ProjectStateStore(fileSystem).LoadAsync(projectDirectory);

        await Assert.That(loaded.IsSuccess).IsFalse();
        await Assert.That(loaded.Error).Contains("aliases are not allowed");
    }

    [Test]
    public async Task Load_WhenProjectConfigurationMatchesEffectiveLimitAcceptsBoundary()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        const int projectManifestByteLimit = 1_048_576;
        const string configurationPrefix = "schemaVersion: 1\nsources: []\npacks: []\n# ";
        var exactConfiguration =
            configurationPrefix
            + new string(
                'x',
                projectManifestByteLimit
                    - System.Text.Encoding.UTF8.GetByteCount(configurationPrefix)
            );
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(exactConfiguration)
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData("schemaVersion: 1\npacks: []\n")
        );
        var stateStore = new ProjectStateStore(fileSystem);
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );

        var loaded = await stateStore.LoadAsync(projectDirectory);
        fileSystem.File.WriteAllText(configurationPath, exactConfiguration + "x");
        var loadedOverLimit = await stateStore.LoadAsync(projectDirectory);

        await Assert
            .That(System.Text.Encoding.UTF8.GetByteCount(exactConfiguration))
            .IsEqualTo(projectManifestByteLimit);
        await Assert.That(loaded.IsSuccess).IsTrue().Because(loaded.Error ?? string.Empty);
        await Assert.That(loadedOverLimit.IsSuccess).IsFalse();
        await Assert.That(loadedOverLimit.Error).Contains("at least 1048577 bytes read");
    }

    [Test]
    public async Task Load_WhenLockFileAtLimitAcceptsBoundaryAndLimitPlusOneFails()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var initialSave = await stateStore.SaveAsync(projectDirectory, CreateValidState());
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        var lockFilePath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.LockFileName
        );
        var lockFileByteCount = fileSystem.File.ReadAllBytes(lockFilePath).Length;
        var stateAtBoundary = CreateValidState() with
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Preferences = new CliPreferences
                {
                    ResourceLimits = new ResourceLimitPreferences
                    {
                        LockFileBytes = lockFileByteCount,
                    },
                },
            },
            EffectiveResourceLimits = ResourceLimits.Default with
            {
                LockFileBytes = lockFileByteCount,
            },
        };
        var boundarySave = await stateStore.SaveAsync(projectDirectory, stateAtBoundary);
        var loadedAtBoundary = await stateStore.LoadAsync(projectDirectory);
        var configuration = fileSystem.File.ReadAllText(configurationPath);
        var loweredConfiguration = configuration.Replace(
            $"lockFileBytes: {lockFileByteCount}",
            $"lockFileBytes: {lockFileByteCount - 1}",
            StringComparison.Ordinal
        );
        var originalLockFile = fileSystem.File.ReadAllText(lockFilePath);
        fileSystem.File.WriteAllText(configurationPath, loweredConfiguration);

        var loadedOverLimit = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(initialSave.IsSuccess).IsTrue();
        await Assert
            .That(boundarySave.IsSuccess)
            .IsTrue()
            .Because(boundarySave.Error ?? string.Empty);
        await Assert
            .That(loadedAtBoundary.IsSuccess)
            .IsTrue()
            .Because(loadedAtBoundary.Error ?? string.Empty);
        await Assert.That(loweredConfiguration).IsNotEqualTo(configuration);
        await Assert.That(loadedOverLimit.IsSuccess).IsFalse();
        await Assert
            .That(loadedOverLimit.Error)
            .Contains($"at least {lockFileByteCount} bytes read");
        await Assert.That(fileSystem.File.ReadAllText(lockFilePath)).IsEqualTo(originalLockFile);
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
    public async Task Save_WhenSerializedDocumentsMatchEffectiveLimits_PersistsAtBoundary()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var state = CreateValidState();
        var initialSave = await stateStore.SaveAsync(projectDirectory, state);
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        var lockFilePath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.LockFileName
        );
        var resourceLimits = ResourceLimits.Default with
        {
            ProjectManifestBytes = fileSystem.File.ReadAllBytes(configurationPath).Length,
            LockFileBytes = fileSystem.File.ReadAllBytes(lockFilePath).Length,
        };

        var saved = await stateStore.SaveAsync(
            projectDirectory,
            state with
            {
                EffectiveResourceLimits = resourceLimits,
            }
        );

        await Assert.That(initialSave.IsSuccess).IsTrue();
        await Assert.That(saved.IsSuccess).IsTrue().Because(saved.Error ?? string.Empty);
    }

    [Test]
    public async Task Save_WhenSerializedConfigurationExceedsLimit_PreservesExistingDocuments()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var initialSave = await stateStore.SaveAsync(projectDirectory, CreateValidState());
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        var lockFilePath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.LockFileName
        );
        var originalConfiguration = fileSystem.File.ReadAllText(configurationPath);
        var originalLockFile = fileSystem.File.ReadAllText(lockFilePath);
        var state = CreateValidState();
        state.Configuration.Variables["padding"] = new string('x', 2_048);
        state = state with
        {
            EffectiveResourceLimits = ResourceLimits.Default with { ProjectManifestBytes = 1_024 },
        };

        var saved = await stateStore.SaveAsync(projectDirectory, state);

        await Assert.That(initialSave.IsSuccess).IsTrue();
        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert.That(saved.Error).Contains("lunapack.yml serialized content exceeds");
        await Assert
            .That(fileSystem.File.ReadAllText(configurationPath))
            .IsEqualTo(originalConfiguration);
        await Assert.That(fileSystem.File.ReadAllText(lockFilePath)).IsEqualTo(originalLockFile);
    }

    [Test]
    public async Task Save_WhenNewProjectLimitIsLower_RecomputesEffectiveLimitBeforeWrite()
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
                Preferences = new CliPreferences
                {
                    ResourceLimits = new ResourceLimitPreferences { ProjectManifestBytes = 1 },
                },
            },
        };

        var saved = await stateStore.SaveAsync(projectDirectory, state);

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert
            .That(saved.Error)
            .Contains("lunapack.yml serialized content exceeds its 1 byte limit");
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
    }

    [Test]
    public async Task Initialize_WhenUserManifestLimitCannotFitInitialState_DoesNotCreateFiles()
    {
        using var workspace = new TestWorkspace();
        var savedSettings = await workspace.UserSettingsStore.SaveAsync(
            new UserSettings
            {
                Global = new UserTrust
                {
                    Preferences = new CliPreferences
                    {
                        ResourceLimits = new ResourceLimitPreferences { ProjectManifestBytes = 1 },
                    },
                },
            }
        );

        var initialized = await workspace.StateStore.InitializeAsync(workspace.Path);

        await Assert.That(savedSettings.IsSuccess).IsTrue();
        await Assert.That(initialized.IsSuccess).IsFalse();
        await Assert.That(initialized.Error).Contains("1 byte limit");
        await Assert
            .That(
                File.Exists(Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName))
            )
            .IsFalse();
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, ProjectStateStore.LockFileName)))
            .IsFalse();
    }

    [Test]
    public async Task Save_WhenLockGraphMatchesDepthLimit_AcceptsBoundaryAndRejectsOneMore()
    {
        var exactFileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        exactFileSystem.AddDirectory(projectDirectory);
        var exactState = CreateLockGraphState(3) with
        {
            EffectiveResourceLimits = ResourceLimits.Default with { DependencyDepth = 2 },
        };

        var exact = await new ProjectStateStore(exactFileSystem).SaveAsync(
            projectDirectory,
            exactState
        );

        var overFileSystem = CreateFileSystem();
        overFileSystem.AddDirectory(projectDirectory);
        var overState = CreateLockGraphState(3) with
        {
            EffectiveResourceLimits = ResourceLimits.Default with { DependencyDepth = 1 },
        };
        var over = await new ProjectStateStore(overFileSystem).SaveAsync(
            projectDirectory,
            overState
        );

        await Assert.That(exact.IsSuccess).IsTrue().Because(exact.Error ?? string.Empty);
        await Assert.That(over.IsSuccess).IsFalse();
        await Assert
            .That(over.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(over.Error).Contains("at least 2 edges observed");
    }

    [Test]
    public async Task Save_WhenSerializedLockFileExceedsLimit_PreservesExistingDocuments()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var initialState = CreateStateWithSourceIdentity("local", "source");
        var initialSave = await stateStore.SaveAsync(projectDirectory, initialState);
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        var lockFilePath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.LockFileName
        );
        var originalConfiguration = fileSystem.File.ReadAllText(configurationPath);
        var originalLockFile = fileSystem.File.ReadAllText(lockFilePath);
        var state = CreateStateWithSourceIdentity("local", "source");
        var destination = new string('x', 2_048);
        state.Configuration.Packs[0] = state.Configuration.Packs[0] with
        {
            Destination = destination,
        };
        state.LockFile.Packs[0] = state.LockFile.Packs[0] with { Destination = destination };
        state = state with
        {
            EffectiveResourceLimits = ResourceLimits.Default with { LockFileBytes = 1_024 },
        };

        var saved = await stateStore.SaveAsync(projectDirectory, state);

        await Assert.That(initialSave.IsSuccess).IsTrue();
        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert.That(saved.Error).Contains("lunapack-lock.yml serialized content exceeds");
        await Assert
            .That(fileSystem.File.ReadAllText(configurationPath))
            .IsEqualTo(originalConfiguration);
        await Assert.That(fileSystem.File.ReadAllText(lockFilePath)).IsEqualTo(originalLockFile);
    }

    [Test]
    public async Task Save_WhenSnapshotExceedsEffectiveLimit_PreservesProjectDocuments()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);
        var initialSave = await stateStore.SaveAsync(projectDirectory, CreateValidState());
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        var lockFilePath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.LockFileName
        );
        var oversizedConfiguration = new string('x', 1_025);
        fileSystem.File.WriteAllText(configurationPath, oversizedConfiguration);
        var originalLockFile = fileSystem.File.ReadAllText(lockFilePath);
        var state = CreateValidState() with
        {
            EffectiveResourceLimits = ResourceLimits.Default with { ProjectManifestBytes = 1_024 },
        };

        var saved = await stateStore.SaveAsync(projectDirectory, state);

        await Assert.That(initialSave.IsSuccess).IsTrue();
        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert.That(saved.Error).Contains("at least 1025 bytes read");
        await Assert
            .That(fileSystem.File.ReadAllText(configurationPath))
            .IsEqualTo(oversizedConfiguration);
        await Assert.That(fileSystem.File.ReadAllText(lockFilePath)).IsEqualTo(originalLockFile);
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
        await Assert.That(saved.Error ?? string.Empty).Contains("lunapack.yml validation failed");
        await Assert.That(saved.Error ?? string.Empty).Contains("schema version must be 1");
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
    public async Task Save_WhenStateContainsOciSource_PersistsCanonicalOriginAndPath()
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
                    new ProjectConfiguration.OciSource
                    {
                        Name = "private",
                        Url = "HTTPS://Registry.Example.TEST:443/team//packs/",
                        Username = "publisher",
                    },
                ],
            },
        };

        var saved = await stateStore.SaveAsync(projectDirectory, state);
        var loaded = await stateStore.LoadAsync(projectDirectory);
        var configuration = fileSystem.File.ReadAllText(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName)
        );

        await Assert.That(saved.IsSuccess).IsTrue().Because(saved.Error ?? string.Empty);
        var source = loaded.RequireValue().Configuration.Sources.Single();
        await Assert.That(source).IsTypeOf<ProjectConfiguration.OciSource>();
        var ociSource = (ProjectConfiguration.OciSource)source;
        await Assert.That(ociSource.Url).IsEqualTo("https://registry.example.test");
        await Assert.That(ociSource.Path).IsEqualTo("team/packs");
        await Assert.That(ociSource.Username).IsEqualTo("publisher");
        await Assert.That(configuration).Contains("url: https://registry.example.test");
        await Assert.That(configuration).Contains("path: team/packs");
        await Assert.That(configuration).DoesNotContain("password");
    }

    [Test]
    [Arguments("ref: main")]
    [Arguments("timeoutSeconds: 120")]
    public async Task Load_WhenOciSourceDeclaresGitOnlyProperties_ReturnsFailure(
        string gitOnlyProperty
    )
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(
                $"schemaVersion: 1\nsources:\n  - name: private\n    type: oci\n    url: https://registry.example.test\n    {gitOnlyProperty}\npacks: []\n"
            )
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData("schemaVersion: 1\npacks: []\n")
        );
        var stateStore = new ProjectStateStore(fileSystem);

        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(loaded.IsSuccess).IsFalse();
        await Assert.That(loaded.Error).Contains("OCI sources do not support");
    }

    [Test]
    public async Task Save_WhenGitProvenanceUrlHasTrailingSlash_PersistsState()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);

        var saved = await stateStore.SaveAsync(
            projectDirectory,
            CreateGitSourceState($"{GetTestGitSourceUrl()}/")
        );
        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(saved.IsSuccess).IsTrue().Because(saved.Error ?? string.Empty);
        var persistedState = loaded.RequireValue();
        await Assert
            .That(persistedState.LockFile.Packs.Single().GitSource?.Url)
            .IsEqualTo($"{GetTestGitSourceUrl()}/");
    }

    [Test]
    public async Task Save_WhenGitRepositoryDoesNotMatchProvenance_ReturnsContextualError()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);

        var saved = await stateStore.SaveAsync(
            projectDirectory,
            CreateGitSourceState(GetTestGitSourceUrl("other.example.test"))
        );
        var error = saved.Error ?? string.Empty;

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert.That(error).Contains("lunapack-lock.yml validation failed");
        await Assert
            .That(error)
            .Contains(
                "Resolved pack 'example' from source 'git' has a Git repository URL mismatch between source identity and provenance."
            );
        await Assert.That(error).DoesNotContain("other.example.test");
        await Assert
            .That(
                fileSystem.File.Exists(
                    fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName)
                )
            )
            .IsFalse();
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

    [Test]
    public async Task Save_WhenResolvedLinkFilesExceedLimit_ReturnsResourceFailure()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var state = CreateStateWithSourceIdentity("local", "source") with
        {
            EffectiveResourceLimits = ResourceLimits.Default with { FilesPerPack = 1 },
        };
        state.Configuration.Links["assets"] = new ProjectConfiguration.Link
        {
            Source = "local",
            Includes = ["**/*"],
        };
        var identity = ConfiguredSourceIdentity.CreateLocal("source");
        state.LockFile.Links["assets"] = new ProjectLockFile.ResolvedLink
        {
            DefinitionSha256 = new string('a', 64),
            SourceIdentity = identity,
            SourceName = "local",
            Files =
            [
                new ProjectLockFile.LinkFile
                {
                    DeclaredTargetPath = "first.txt",
                    Sha256 = new string('b', 64),
                    SourcePath = "first.txt",
                    TargetPath = "first.txt",
                },
                new ProjectLockFile.LinkFile
                {
                    DeclaredTargetPath = "second.txt",
                    Sha256 = new string('c', 64),
                    SourcePath = "second.txt",
                    TargetPath = "second.txt",
                },
            ],
        };

        var saved = await new ProjectStateStore(fileSystem).SaveAsync(projectDirectory, state);

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert
            .That(saved.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded)
            .Because(saved.Error ?? string.Empty);
        await Assert.That(saved.Error).Contains("link 'assets'");
        await Assert
            .That(
                fileSystem.File.Exists(
                    fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName)
                )
            )
            .IsFalse();
    }

    [Test]
    public async Task Save_WhenPackAndLinkFilesShareGraphLimit_RejectsCombinedCount()
    {
        var fileSystem = CreateFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var state = CreateStateWithSourceIdentity("local", "source") with
        {
            EffectiveResourceLimits = ResourceLimits.Default with
            {
                FilesPerGraph = 1,
                FilesPerPack = 2,
            },
        };
        state
            .LockFile.Packs[0]
            .ManagedFiles.Add(
                new ProjectLockFile.ManagedFile
                {
                    DeclaredTargetPath = "pack.txt",
                    Sha256 = new string('d', 64),
                    TargetPath = "pack.txt",
                }
            );
        state.Configuration.Links["assets"] = new ProjectConfiguration.Link
        {
            Source = "local",
            Includes = ["**/*"],
        };
        state.LockFile.Links["assets"] = new ProjectLockFile.ResolvedLink
        {
            DefinitionSha256 = new string('a', 64),
            SourceIdentity = ConfiguredSourceIdentity.CreateLocal("source"),
            SourceName = "local",
            Files =
            [
                new ProjectLockFile.LinkFile
                {
                    DeclaredTargetPath = "link.txt",
                    Sha256 = new string('b', 64),
                    SourcePath = "link.txt",
                    TargetPath = "link.txt",
                },
            ],
        };

        var saved = await new ProjectStateStore(fileSystem).SaveAsync(projectDirectory, state);

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert
            .That(saved.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded)
            .Because(saved.Error ?? string.Empty);
        await Assert.That(saved.Error).Contains("managed and linked files");
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

    private static ProjectState CreateGitSourceState(string provenanceUrl)
    {
        var source = new ProjectConfiguration.GitSource
        {
            Name = "git",
            Url = GetTestGitSourceUrl(),
            Ref = "main",
            Path = "packs",
            TimeoutSeconds = 120,
        };

        return new ProjectState
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Sources = [source],
                Packs =
                [
                    new ProjectConfiguration.RequestedPack { Id = "example", Version = "1.0.0" },
                ],
            },
            LockFile = new ProjectLockFile
            {
                SchemaVersion = 1,
                Packs =
                [
                    new ProjectLockFile.ResolvedPack
                    {
                        Id = "example",
                        Version = "1.0.0",
                        SourceName = source.Name,
                        SourceIdentity = ConfiguredSourceIdentity.Create(source),
                        GitSource = new GitSourceProvenance
                        {
                            Url = provenanceUrl,
                            Ref = source.Ref,
                            Path = source.Path,
                            ResolvedCommit = "0123456789abcdef0123456789abcdef01234567",
                        },
                        PackPath = "example",
                    },
                ],
            },
        };
    }

    private static ProjectState CreateLockGraphState(int packCount)
    {
        var packs = Enumerable
            .Range(0, packCount)
            .Select(index => new ProjectLockFile.ResolvedPack
            {
                Id = $"pack-{index}",
                Version = "1.0.0",
                SourceName = "local",
                SourceIdentity = ConfiguredSourceIdentity.CreateLocal("source"),
                SourcePath = "source",
                PackPath = $"pack-{index}",
                Packs =
                    index + 1 < packCount
                        ?
                        [
                            new ProjectLockFile.PackReference
                            {
                                Id = $"pack-{index + 1}",
                                Version = "1.0.0",
                            },
                        ]
                        : [],
            })
            .ToList();
        return new ProjectState
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Sources =
                [
                    new ProjectConfiguration.LocalSource { Name = "local", Path = "source" },
                ],
                Packs =
                [
                    new ProjectConfiguration.RequestedPack { Id = "pack-0", Version = "1.0.0" },
                ],
            },
            LockFile = new ProjectLockFile { SchemaVersion = 1, Packs = packs },
        };
    }

    private static ProjectState CreateValidState() =>
        new()
        {
            Configuration = new ProjectConfiguration { SchemaVersion = 1 },
            LockFile = new ProjectLockFile { SchemaVersion = 1 },
        };

    private static string GetTestGitSourceUrl(string host = "example.test") =>
        $"https://{host}/packs.git";

    private static MockFileSystem CreateFileSystem() => new();
}
