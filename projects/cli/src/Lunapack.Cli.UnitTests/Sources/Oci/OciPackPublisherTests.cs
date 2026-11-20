using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Authoring;
using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Sources.Oci;
using Lunapack.Cli.Trust;
using Lunapack.Cli.UnitTests.TestSupport;

namespace Lunapack.Cli.UnitTests.Sources.Oci;

[NotInParallel]
public sealed class OciPackPublisherTests
{
    private static readonly string _projectDirectory = OperatingSystem.IsWindows()
        ? @"C:\project"
        : "/project";
    private static readonly string _packDirectory = OperatingSystem.IsWindows()
        ? @"C:\project\checkout"
        : "/project/checkout";

    [Test]
    public async Task PublishAsync_WhenPackIsValid_PublishesAndVerifiesImmutableRelease()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient();
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, coordinator);
        var source = CreateSource();

        var result = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            source,
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        var publication = result.Value.RequireNotNull();
        await Assert.That(publication.PackId).IsEqualTo("backend");
        await Assert.That(publication.Version).IsEqualTo("1.0.0");
        await Assert
            .That(publication.Reference)
            .IsEqualTo("registry.example.test/team/packs/backend:1.0.0");
        await Assert
            .That(publication.ManifestDigest)
            .IsEqualTo(registry.ManifestDescriptor?.Digest);
        await Assert.That(registry.PushedBlobCount).IsEqualTo(2);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(1);
        await Assert
            .That(
                fileSystem.File.Exists(
                    fileSystem.Path.Combine(
                        _projectDirectory,
                        ProjectStateStore.ConfigurationFileName
                    )
                )
            )
            .IsFalse();
        await Assert.That(GetOciTemporaryDirectories(fileSystem)).IsEmpty();
    }

    [Test]
    public async Task PublishAsync_WhenEffectiveFileLimitIsLower_RejectsBeforeRegistryUpload()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient();
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, coordinator);

        var result = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            ResourceLimits.Default with
            {
                FilesPerPack = 1,
            },
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert
            .That(result.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(registry.PushedBlobCount).IsEqualTo(0);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(0);
    }

    [Test]
    public async Task Scenario_Publish_WhenGitMetadataIsAvailable_AddsSanitizedSourceAnnotations()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient();
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(
            fileSystem,
            registry,
            coordinator,
            new PackSourceMetadataGitProcessRunner()
        );

        var result = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );
        using var manifest = JsonDocument.Parse(registry.ManifestContent!);
        var annotations = manifest.RootElement.GetProperty("annotations");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(annotations.GetProperty("org.opencontainers.image.source").GetString())
            .IsEqualTo("https://github.com/lunarisdigitalsolutions/private-packs.git");
        await Assert
            .That(annotations.GetProperty("org.opencontainers.image.revision").GetString())
            .IsEqualTo("abcdef0123456789abcdef0123456789abcdef01");
        await Assert.That(manifest.RootElement.GetRawText()).DoesNotContain("secret-sentinel");
    }

    [Test]
    public async Task Scenario_ResolveLockedOci_DoesNotReturnDisposedWorkspacePaths()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient();
        using var publisherCoordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, publisherCoordinator);
        var source = CreateSource();
        var publication = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            source,
            CancellationToken.None
        );
        var manifest = OciPackArtifact.ReadManifest(registry.ManifestContent!).RequireValue();
        var provenance = new OciSourceProvenance
        {
            RegistryUrl = source.Url,
            Repository = OciRepositoryTagMapper.CreateRepository(source, "backend").RequireValue(),
            Tag = OciRepositoryTagMapper.CreateTag("1.0.0").RequireValue(),
            ManifestDigest = registry.ManifestDescriptor!.Digest,
            ContentDigest = manifest.Content.Digest,
            ContentSize = manifest.Content.Size,
        };
        using var ociPackSource = new OciPackSource(
            fileSystem,
            TestConsole.Create(),
            registry,
            new FakeCredentialStore(),
            new FakeCredentialPrompt(null)
        );
        using var catalog = new PackCatalog(
            fileSystem,
            TestConsole.Create(),
            ociPackSource: ociPackSource,
            operationSnapshotSecurity: new NoOpOperationSnapshotSecurity()
        );
        var lockedPack = new ProjectLockFile.ResolvedPack
        {
            Id = "backend",
            Version = "1.0.0",
            PackPath = "backend",
            SourceIdentity = ConfiguredSourceIdentity.Create(source),
            SourceName = source.Name,
            OciSource = provenance,
        };

        var resolved = await catalog.ResolveLockedOciAsync(
            _projectDirectory,
            new ProjectConfiguration { Sources = [source] },
            lockedPack
        );
        var exhaustedBudget = new CatalogManifestBudget(ResourceLimits.Default);
        exhaustedBudget.Observe(CatalogManifestBudget.MaximumManifestBytes);
        var overBudget = await catalog.ResolveLockedOciAsync(
            _projectDirectory,
            new ProjectConfiguration { Sources = [source] },
            lockedPack,
            workBudget: exhaustedBudget
        );

        await Assert.That(publication.IsSuccess).IsTrue();
        await Assert.That(resolved.IsSuccess).IsTrue().Because(resolved.Error ?? string.Empty);
        await Assert.That(resolved.RequireValue().SourcePath).IsEmpty();
        await Assert.That(resolved.RequireValue().PackDirectory).IsEmpty();
        await Assert.That(overBudget.IsSuccess).IsFalse();
        await Assert
            .That(overBudget.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(GetOciTemporaryDirectories(fileSystem)).IsEmpty();
    }

    [Test]
    public async Task ResolveLockedOci_WhenDownloadExceedsEffectiveLimit_StopsBeforeContentFetch()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient();
        using var publisherCoordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, publisherCoordinator);
        var source = CreateSource();
        var publication = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            source,
            CancellationToken.None
        );
        var manifest = OciPackArtifact.ReadManifest(registry.ManifestContent!).RequireValue();
        var provenance = new OciSourceProvenance
        {
            RegistryUrl = source.Url,
            Repository = OciRepositoryTagMapper.CreateRepository(source, "backend").RequireValue(),
            Tag = OciRepositoryTagMapper.CreateTag("1.0.0").RequireValue(),
            ManifestDigest = registry.ManifestDescriptor!.Digest,
            ContentDigest = manifest.Content.Digest,
            ContentSize = manifest.Content.Size,
        };
        using var ociPackSource = new OciPackSource(
            fileSystem,
            TestConsole.Create(),
            registry,
            new FakeCredentialStore(),
            new FakeCredentialPrompt(null)
        );
        using var catalog = new PackCatalog(
            fileSystem,
            TestConsole.Create(),
            ociPackSource: ociPackSource,
            operationSnapshotSecurity: new NoOpOperationSnapshotSecurity()
        );
        var lockedPack = new ProjectLockFile.ResolvedPack
        {
            Id = "backend",
            Version = "1.0.0",
            PackPath = "backend",
            SourceIdentity = ConfiguredSourceIdentity.Create(source),
            SourceName = source.Name,
            OciSource = provenance,
        };
        var resourceLimits = ResourceLimits.Default with
        {
            OciDownloadBytes = manifest.Content.Size - 1,
        };

        var resolved = await catalog.ResolveLockedOciAsync(
            _projectDirectory,
            new ProjectConfiguration { Sources = [source] },
            lockedPack,
            resourceLimits: resourceLimits
        );

        await Assert.That(publication.IsSuccess).IsTrue();
        await Assert.That(resolved.IsSuccess).IsFalse();
        await Assert
            .That(resolved.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert
            .That(resolved.Error)
            .Contains($"at least {manifest.Content.Size} bytes declared");
        await Assert.That(registry.FetchedBlobCount).IsEqualTo(0);
        await Assert.That(GetOciTemporaryDirectories(fileSystem)).IsEmpty();
    }

    [Test]
    public async Task Scenario_BrowseAndResolve_WhenOciSourceIsIgnored_SkipsRegistryAndCredentials()
    {
        using var workspace = new TestWorkspace();
        var fileSystem = workspace.FileSystem;
        var projectDirectory = workspace.Path;
        var source = CreateSource();
        var registry = new FakeOciRegistryClient();
        var store = new FakeCredentialStore();
        var prompt = new FakeCredentialPrompt(null);
        var settingsStore = new UserSettingsStore(
            fileSystem,
            fileSystem.Path.Combine(projectDirectory, "profile")
        );
        var projectKey = settingsStore.GetProjectKey(projectDirectory).RequireValue();
        var sourceIgnoreStore = new SourceIgnoreStore(fileSystem, settingsStore.SettingsDirectory);
        var savedSettings = await sourceIgnoreStore.SaveAsync(
            new SourceIgnoreSettings
            {
                Projects = { [projectKey] = [ConfiguredSourceIdentity.Create(source)] },
            }
        );
        using var ociPackSource = new OciPackSource(
            fileSystem,
            TestConsole.Create(),
            registry,
            store,
            prompt
        );
        using var catalog = new PackCatalog(
            fileSystem,
            TestConsole.Create(),
            ociPackSource: ociPackSource,
            configuredSourceIgnoreService: new ConfiguredSourceIgnoreService(
                fileSystem,
                settingsStore
            ),
            operationSnapshotSecurity: new NoOpOperationSnapshotSecurity()
        );
        var configuration = new ProjectConfiguration { Sources = [source] };

        var browsed = await catalog.BrowseAsync(projectDirectory, configuration);
        var resolved = await catalog.ResolveAsync(
            projectDirectory,
            configuration,
            "backend",
            requestedVersion: null
        );

        await Assert
            .That(savedSettings.IsSuccess)
            .IsTrue()
            .Because(savedSettings.Error ?? string.Empty);
        await Assert.That(browsed.IsSuccess).IsTrue();
        await Assert.That(browsed.RequireValue()).IsEmpty();
        await Assert.That(resolved.IsSuccess).IsFalse();
        await Assert.That(registry.RequestCalls).IsEqualTo(0);
        await Assert.That(store.GetCalls).IsEqualTo(0);
        await Assert.That(prompt.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task GetAvailableAsync_WhenRepositoryEnumerationIsDenied_FailsClosed()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient();
        var source = CreateSource();
        var currentRelease = await PublishPackVersionAsync(fileSystem, registry, source, "1.0.0");
        await PublishPackVersionAsync(fileSystem, registry, source, "1.1.0");
        registry.DenyRepositoryEnumeration = true;

        var stateStore = new ProjectStateStore(fileSystem);
        var stateSaved = await stateStore.SaveAsync(
            _projectDirectory,
            CreateOciProjectState(source, currentRelease)
        );
        var ansiConsole = new Spectre.Console.Testing.TestConsole();
        var console = new CliConsole(ansiConsole, CliLogLevel.Info);
        using var ociPackSource = new OciPackSource(
            fileSystem,
            console,
            registry,
            new FakeCredentialStore(),
            new FakeCredentialPrompt(null),
            operationSnapshotSecurity: new NoOpOperationSnapshotSecurity()
        );
        using var catalog = new PackCatalog(
            fileSystem,
            console,
            ociPackSource: ociPackSource,
            operationSnapshotSecurity: new NoOpOperationSnapshotSecurity()
        );
        var lifecycle = CreatePackLifecycleService(fileSystem, catalog, stateStore, console);
        var selectionService = new PackUpdateSelectionService(catalog, stateStore, lifecycle);

        var availability = await selectionService.GetAvailableAsync(_projectDirectory);

        await Assert.That(stateSaved.IsSuccess).IsTrue().Because(stateSaved.Error ?? string.Empty);
        await Assert
            .That(availability.IsSuccess)
            .IsFalse()
            .Because(availability.Error ?? string.Empty);
        await Assert.That(availability.Error).Contains("discovery is incomplete");
        await Assert.That(ansiConsole.Output).Contains("catalog enumeration");
        await Assert.That(ansiConsole.Output).Contains("incomplete");
    }

    [Test]
    public async Task PublishAsync_WhenReleaseTagAlreadyExists_DoesNotPushAgain()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient();
        registry.SeedManifest("team/packs/backend", "1.0.0", "sha256:existing");
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, coordinator);

        var result = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("already exists");
        await Assert.That(registry.PushedBlobCount).IsEqualTo(0);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(0);
    }

    [Test]
    public async Task PublishAsync_WhenExistingTagMatchesContent_DoesNotOverwriteIt()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient();
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, coordinator);

        var firstPublication = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );
        var pushedBlobCount = registry.PushedBlobCount;
        var pushedManifestCount = registry.PushedManifestCount;
        var duplicatePublication = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );

        await Assert.That(firstPublication.IsSuccess).IsTrue();
        await Assert.That(duplicatePublication.IsSuccess).IsFalse();
        await Assert.That(duplicatePublication.Error).Contains("already exists");
        await Assert.That(registry.PushedBlobCount).IsEqualTo(pushedBlobCount);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(pushedManifestCount);
    }

    [Test]
    public async Task PublishAsync_WhenConcurrentPublishersRace_ImmutableRegistryRejectsOne()
    {
        var firstFileSystem = CreateFileSystem();
        var secondFileSystem = CreateFileSystem();
        secondFileSystem.File.WriteAllText(
            secondFileSystem.Path.Combine(_packDirectory, "README.md"),
            "A different release payload."
        );
        var registry = new FakeOciRegistryClient
        {
            EnforceImmutableTags = true,
            SynchronizeInitialTagLookups = true,
        };
        using var firstCoordinator = CreateCoordinator();
        using var secondCoordinator = CreateCoordinator();
        var firstPublisher = CreatePublisher(firstFileSystem, registry, firstCoordinator);
        var secondPublisher = CreatePublisher(secondFileSystem, registry, secondCoordinator);

        var results = await Task.WhenAll(
            firstPublisher.PublishAsync(
                _projectDirectory,
                _packDirectory,
                CreateSource(),
                CancellationToken.None
            ),
            secondPublisher.PublishAsync(
                _projectDirectory,
                _packDirectory,
                CreateSource(),
                CancellationToken.None
            )
        );

        await Assert.That(results.Count(result => result.IsSuccess)).IsEqualTo(1);
        await Assert.That(results.Count(result => !result.IsSuccess)).IsEqualTo(1);
        await Assert
            .That(results.Single(result => !result.IsSuccess).Error)
            .Contains("conflicting update");
        await Assert.That(registry.PushedManifestCount).IsEqualTo(1);
        await Assert.That(GetOciTemporaryDirectories(firstFileSystem)).IsEmpty();
        await Assert.That(GetOciTemporaryDirectories(secondFileSystem)).IsEmpty();
    }

    [Test]
    public async Task PublishAsync_WhenExistingTagHasAnotherArtifact_DoesNotOverwriteIt()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient();
        registry.SeedManifest("team/packs/backend", "1.0.0", "sha256:other-artifact");
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, coordinator);

        var result = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("already exists");
        await Assert.That(registry.PushedBlobCount).IsEqualTo(0);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(0);
    }

    [Test]
    public async Task PublishAsync_WhenCompletionDigestDiffers_FailsWithoutSuccess()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient { CompletionDigestOverride = "sha256:different" };
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, coordinator);

        var result = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("different digest");
        await Assert.That(registry.PushedManifestCount).IsEqualTo(1);
        await Assert.That(GetOciTemporaryDirectories(fileSystem)).IsEmpty();
    }

    [Test]
    public async Task PublishAsync_WhenRegistryRequiresAuthentication_ResumesAndPersistsCredential()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient { RequireAuthentication = true };
        var store = new FakeCredentialStore();
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("publisher", "secret"));
        var ansiConsole = new Spectre.Console.Testing.TestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        using var coordinator = CreateCoordinator(
            store,
            prompt,
            new CliConsole(ansiConsole, CliLogLevel.Info)
        );
        var publisher = CreatePublisher(fileSystem, registry, coordinator);

        var result = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert.That(prompt.Calls).IsEqualTo(1);
        await Assert.That(store.SaveCalls).IsEqualTo(1);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(1);
    }

    [Test]
    public async Task PublishAsync_WhenRegistryDeniesPush_DoesNotPromptOrReportSuccess()
    {
        var fileSystem = CreateFileSystem();
        var registry = new FakeOciRegistryClient { DenyPush = true };
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("publisher", "secret"));
        using var coordinator = CreateCoordinator(prompt: prompt);
        var publisher = CreatePublisher(fileSystem, registry, coordinator);

        var result = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("denied");
        await Assert.That(prompt.Calls).IsEqualTo(0);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(0);
    }

    [Test]
    public async Task PublishAsync_WhenManifestIsInvalid_FailsBeforeRegistryAccess()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.File.WriteAllText(
            fileSystem.Path.Combine(_packDirectory, PackManifestStore.FileName),
            "id: invalid id\nversion: 1.0.0\nauthor: Test Author\nlicense: MIT\n"
        );
        var registry = new FakeOciRegistryClient();
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, coordinator);

        var result = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(registry.PushedBlobCount).IsEqualTo(0);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(0);
    }

    [Test]
    public async Task PublishAsync_WhenManagedSourceHasHighConfidenceFinding_RequiresOverride()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.File.WriteAllText(
            fileSystem.Path.Combine(_packDirectory, PackManifestStore.FileName),
            "id: backend\nversion: 1.0.0\nauthor: Test Author\nlicense: MIT\nmanagedFiles:\n  - source: README.md\n    target: docs/README.md\n"
        );
        fileSystem.File.WriteAllBytes(
            fileSystem.Path.Combine(_packDirectory, "README.md"),
            [0x73, 0x61, 0x66, 0x65, 0x00, 0xE2, 0x80, 0xAE]
        );
        var registry = new FakeOciRegistryClient();
        var ansiConsole = new Spectre.Console.Testing.TestConsole();
        ansiConsole.Profile.Width = 500;
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(
            fileSystem,
            registry,
            coordinator,
            console: new CliConsole(ansiConsole, CliLogLevel.Info)
        );

        var blocked = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );

        await Assert.That(blocked.IsSuccess).IsFalse();
        await Assert.That(blocked.Error).Contains("--allow-hidden-characters");
        await Assert.That(registry.RequestCalls).IsEqualTo(0);
        await Assert.That(registry.PushedBlobCount).IsEqualTo(0);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("Hidden Unicode in 'README.md'");
        await Assert.That(ansiConsole.Output).Contains("U+202E (high-confidence)");

        var allowed = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            allowHiddenCharacters: true
        );

        await Assert.That(allowed.IsSuccess).IsTrue().Because(allowed.Error ?? string.Empty);
        await Assert.That(registry.PushedBlobCount).IsEqualTo(2);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(1);
    }

    [Test]
    public async Task PublishAsync_WhenManagedSourceHasWarningFinding_Continues()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.File.WriteAllText(
            fileSystem.Path.Combine(_packDirectory, PackManifestStore.FileName),
            "id: backend\nversion: 1.0.0\nauthor: Test Author\nlicense: MIT\nmanagedFiles:\n  - source: README.md\n    target: docs/README.md\n"
        );
        fileSystem.File.WriteAllText(
            fileSystem.Path.Combine(_packDirectory, "README.md"),
            "safe\u00ADtext"
        );
        var registry = new FakeOciRegistryClient();
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, coordinator);

        var result = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            CreateSource(),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert.That(registry.PushedManifestCount).IsEqualTo(1);
    }

    private static OciPackPublisher CreatePublisher(
        MockFileSystem fileSystem,
        FakeOciRegistryClient registry,
        OciSourceCredentialCoordinator coordinator,
        IGitProcessRunner? sourceMetadataRunner = null,
        CliConsole? console = null
    )
    {
        var gitRunner = new StubGitProcessRunner();
        var gitRefResolver = new GitRefResolver(gitRunner);
        var transportPolicy = TestGitTransportPolicy.Create(fileSystem);
        var authoringValidationService = new PackAuthoringValidationService(
            fileSystem,
            new ExternalSourceRequirementPlanner(gitRefResolver, transportPolicy),
            new ExternalSourceMaterializer(
                fileSystem,
                gitRunner,
                gitRefResolver,
                transportPolicy,
                new NoOpOperationSnapshotSecurity()
            ),
            new PackInstallationPlanner(fileSystem, new PackTemplateRenderer(fileSystem)),
            console ?? TestConsole.Create()
        );
        return new OciPackPublisher(
            fileSystem,
            registry,
            coordinator,
            new PackManifestStore(fileSystem),
            authoringValidationService,
            sourceMetadataRunner ?? new StubGitProcessRunner(),
            new NoOpOperationSnapshotSecurity()
        );
    }

    private static async Task<OciSourceProvenance> PublishPackVersionAsync(
        MockFileSystem fileSystem,
        FakeOciRegistryClient registry,
        ProjectConfiguration.OciSource source,
        string version
    )
    {
        var manifestPath = fileSystem.Path.Combine(_packDirectory, PackManifestStore.FileName);
        fileSystem.File.WriteAllText(
            manifestPath,
            fileSystem
                .File.ReadAllText(manifestPath)
                .Replace("version: 1.0.0", $"version: {version}", StringComparison.Ordinal)
        );
        using var coordinator = CreateCoordinator();
        var publisher = CreatePublisher(fileSystem, registry, coordinator);
        var publication = await publisher.PublishAsync(
            _projectDirectory,
            _packDirectory,
            source,
            CancellationToken.None
        );
        await Assert
            .That(publication.IsSuccess)
            .IsTrue()
            .Because(publication.Error ?? string.Empty);

        var publishedPack = publication.Value.RequireNotNull();
        var artifact = OciPackArtifact.ReadManifest(registry.ManifestContent!).RequireValue();
        return new OciSourceProvenance
        {
            RegistryUrl = source.Url,
            Repository = OciRepositoryTagMapper.CreateRepository(source, "backend").RequireValue(),
            Tag = OciRepositoryTagMapper.CreateTag(version).RequireValue(),
            ManifestDigest = publishedPack.ManifestDigest,
            ContentDigest = artifact.Content.Digest,
            ContentSize = artifact.Content.Size,
        };
    }

    private static ProjectState CreateOciProjectState(
        ProjectConfiguration.OciSource source,
        OciSourceProvenance lockedRelease
    ) =>
        new()
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Sources = [source],
                Packs = [new ProjectConfiguration.RequestedPack { Id = "backend" }],
            },
            LockFile = new ProjectLockFile
            {
                SchemaVersion = 1,
                Packs =
                [
                    new ProjectLockFile.ResolvedPack
                    {
                        Id = "backend",
                        Version = "1.0.0",
                        PackPath = ".",
                        SourceName = source.Name,
                        SourceIdentity = ConfiguredSourceIdentity.Create(source),
                        OciSource = lockedRelease,
                    },
                ],
            },
        };

    private static PackLifecycleService CreatePackLifecycleService(
        MockFileSystem fileSystem,
        PackCatalog catalog,
        ProjectStateStore stateStore,
        CliConsole console
    ) =>
        new(
            fileSystem,
            new CompositePackGraphResolver(catalog),
            new PackInstallationPlanner(fileSystem, new PackTemplateRenderer(fileSystem)),
            new PackUpdatePlanner(fileSystem),
            new PackUpdateTransaction(fileSystem, console),
            stateStore,
            console,
            TestGitTransportPolicy.Create(fileSystem),
            new LifecycleHookPolicyStore(fileSystem, @"C:\profile\.lunapack")
        );

    private static OciSourceCredentialCoordinator CreateCoordinator(
        FakeCredentialStore? store = null,
        FakeCredentialPrompt? prompt = null,
        CliConsole? console = null
    )
    {
        var fileSystem = CreateFileSystem();
        return new OciSourceCredentialCoordinator(
            fileSystem,
            store ?? new FakeCredentialStore(),
            prompt ?? new FakeCredentialPrompt(null),
            console ?? TestConsole.Create()
        );
    }

    private static MockFileSystem CreateFileSystem()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(_projectDirectory);
        fileSystem.AddDirectory(fileSystem.Path.GetTempPath());
        fileSystem.AddDirectory(
            fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "lunapack", "oci")
        );
        fileSystem.AddDirectory(Path.GetTempPath());
        fileSystem.AddFile(
            fileSystem.Path.Combine(_packDirectory, PackManifestStore.FileName),
            new MockFileData("id: backend\nversion: 1.0.0\nauthor: Test Author\nlicense: MIT\n")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(_packDirectory, "README.md"),
            new MockFileData("Private backend pack.")
        );
        return fileSystem;
    }

    private static ProjectConfiguration.OciSource CreateSource() =>
        new()
        {
            Name = "private",
            Url = "https://registry.example.test",
            Path = "team/packs",
        };

    private static IEnumerable<string> GetOciTemporaryDirectories(MockFileSystem fileSystem)
    {
        var temporaryRoot = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            "lunapack",
            "oci"
        );
        return fileSystem.Directory.Exists(temporaryRoot)
            ? fileSystem.Directory.EnumerateDirectories(temporaryRoot)
            : [];
    }

    private sealed class NoOpOperationSnapshotSecurity : IOperationSnapshotSecurity
    {
        public void ApplyDirectory(string path) { }

        public void ApplyFile(string path) { }

        public void MakeReadOnly(IFileSystem fileSystem, string root) { }

        public void PrepareForDelete(IFileSystem fileSystem, string root) { }
    }

    private sealed class FakeCredentialStore : ISourceCredentialStore
    {
        public int GetCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public ManifestOperationResult<OciRegistryCredential?> Get(string key)
        {
            GetCalls++;
            return ManifestOperationResult<OciRegistryCredential?>.Success(null);
        }

        public ManifestOperationResult<bool> Remove(string key) =>
            ManifestOperationResult<bool>.Success(true);

        public ManifestOperationResult<bool> Save(string key, OciRegistryCredential credential)
        {
            SaveCalls++;
            return ManifestOperationResult<bool>.Success(true);
        }
    }

    private sealed class FakeCredentialPrompt(OciRegistryCredential? credential)
        : IOciCredentialPrompt
    {
        public int Calls { get; private set; }

        public ManifestOperationResult<OciRegistryCredential?> Prompt(
            ProjectConfiguration.OciSource source
        )
        {
            Calls++;
            return ManifestOperationResult<OciRegistryCredential?>.Success(credential);
        }
    }

    private sealed class PackSourceMetadataGitProcessRunner : IGitProcessRunner
    {
        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            var output = arguments.Contains("remote", StringComparer.Ordinal)
                ? "https://publisher:secret-sentinel@github.com/lunarisdigitalsolutions/private-packs.git?token=secret-sentinel"
                : "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
            return Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(output, string.Empty)
                )
            );
        }
    }

    private sealed class FakeOciRegistryClient : IOciRegistryClient
    {
        private readonly Lock _stateLock = new();
        private readonly TaskCompletionSource<bool> _initialTagLookups = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> _initialTagLookupReads = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _initialTagLookupCount;
        private int _initialTagLookupReadCount;
        private readonly Dictionary<string, OciRegistryDescriptor> _manifests = new(
            StringComparer.Ordinal
        );
        private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, OciRegistryManifest> _publishedManifests = new(
            StringComparer.Ordinal
        );
        private readonly string _tag = "1.0.0";

        public bool RequireAuthentication { get; init; }

        public bool DenyRepositoryEnumeration { get; set; }

        public bool DenyPush { get; init; }

        public bool EnforceImmutableTags { get; init; }

        public bool SynchronizeInitialTagLookups { get; init; }

        public string? CompletionDigestOverride { get; init; }

        public int RequestCalls { get; private set; }

        public int PushedBlobCount { get; private set; }

        public int PushedManifestCount { get; private set; }

        public int FetchedBlobCount { get; private set; }

        public OciRegistryDescriptor? ManifestDescriptor { get; private set; }

        public byte[]? ManifestContent { get; private set; }

        public void SeedManifest(string repository, string tag, string digest)
        {
            lock (_stateLock)
            {
                _manifests[$"{repository}:{tag}"] = new OciRegistryDescriptor(
                    OciPackMediaTypes.Manifest,
                    digest,
                    1
                );
            }
        }

        public Task<OciRegistryResult<IReadOnlyList<string>>> ListRepositoriesAsync(
            Uri registry,
            OciRegistryCredential? credential,
            string credentialPartition,
            long maximumRecords,
            long maximumBytes,
            CancellationToken cancellationToken
        )
        {
            RequestCalls++;
            return Task.FromResult(
                DenyRepositoryEnumeration
                    ? OciRegistryResult<IReadOnlyList<string>>.Failed(
                        new(OciRegistryFailureKind.AuthorizationDenied, "catalog access denied")
                    )
                    : OciRegistryResult<IReadOnlyList<string>>.Succeeded([])
            );
        }

        public Task<OciRegistryResult<IReadOnlyList<string>>> ListTagsAsync(
            Uri registry,
            string repository,
            OciRegistryCredential? credential,
            string credentialPartition,
            long maximumRecords,
            long maximumBytes,
            CancellationToken cancellationToken
        )
        {
            RequestCalls++;
            var prefix = $"{repository}:";
            IReadOnlyList<string> tags;
            lock (_stateLock)
            {
                tags =
                [
                    .. _manifests
                        .Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                        .Select(key => key[prefix.Length..])
                        .OrderBy(tag => tag, StringComparer.Ordinal),
                ];
            }

            return Task.FromResult(OciRegistryResult<IReadOnlyList<string>>.Succeeded(tags));
        }

        public Task<OciRegistryResult<OciRegistryManifest>> FetchManifestAsync(
            Uri registry,
            string repository,
            string reference,
            OciRegistryCredential? credential,
            string credentialPartition,
            CancellationToken cancellationToken
        )
        {
            RequestCalls++;
            var manifest = _publishedManifests
                .FirstOrDefault(entry =>
                    entry.Key.StartsWith($"{repository}:", StringComparison.Ordinal)
                    && (
                        string.Equals(
                            entry.Key[(repository.Length + 1)..],
                            reference,
                            StringComparison.Ordinal
                        )
                        || string.Equals(
                            entry.Value.Descriptor.Digest,
                            reference,
                            StringComparison.Ordinal
                        )
                    )
                )
                .Value;
            return Task.FromResult(
                manifest is null
                    ? OciRegistryResult<OciRegistryManifest>.Failed(
                        new(OciRegistryFailureKind.NotFound, "not found")
                    )
                    : OciRegistryResult<OciRegistryManifest>.Succeeded(manifest)
            );
        }

        public Task<OciRegistryResult<OciBlobTransfer>> FetchBlobAsync(
            Uri registry,
            string repository,
            OciRegistryDescriptor descriptor,
            Stream destination,
            long maximumBytes,
            OciRegistryCredential? credential,
            string credentialPartition,
            CancellationToken cancellationToken
        )
        {
            RequestCalls++;
            FetchedBlobCount++;
            if (
                !_blobs.TryGetValue($"{repository}:{descriptor.Digest}", out var bytes)
                || bytes.LongLength > maximumBytes
            )
            {
                return Task.FromResult(
                    OciRegistryResult<OciBlobTransfer>.Failed(
                        new(OciRegistryFailureKind.NotFound, "not found")
                    )
                );
            }

            return CopyBlobAsync(bytes, destination, cancellationToken);
        }

        public async Task<OciRegistryResult<OciRegistryUnit>> PushBlobAsync(
            Uri registry,
            string repository,
            OciRegistryDescriptor descriptor,
            Stream content,
            OciRegistryCredential? credential,
            string credentialPartition,
            CancellationToken cancellationToken
        )
        {
            RequestCalls++;
            if (GetPushFailure(credential) is { } failure)
            {
                return OciRegistryResult<OciRegistryUnit>.Failed(failure);
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            if (!OciPackArtifact.MatchesDescriptor(bytes, descriptor))
            {
                return OciRegistryResult<OciRegistryUnit>.Failed(
                    new(OciRegistryFailureKind.InvalidResponse, "blob descriptor mismatch")
                );
            }

            lock (_stateLock)
            {
                _blobs[$"{repository}:{descriptor.Digest}"] = bytes;
                PushedBlobCount++;
            }

            return OciRegistryResult<OciRegistryUnit>.Succeeded(new());
        }

        public async Task<OciRegistryResult<OciRegistryUnit>> PushManifestAsync(
            Uri registry,
            string repository,
            OciRegistryDescriptor descriptor,
            Stream content,
            string tag,
            OciRegistryCredential? credential,
            string credentialPartition,
            CancellationToken cancellationToken
        )
        {
            RequestCalls++;
            if (GetPushFailure(credential) is { } failure)
            {
                return OciRegistryResult<OciRegistryUnit>.Failed(failure);
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            if (!OciPackArtifact.MatchesDescriptor(buffer.ToArray(), descriptor))
            {
                return OciRegistryResult<OciRegistryUnit>.Failed(
                    new(OciRegistryFailureKind.InvalidResponse, "manifest descriptor mismatch")
                );
            }

            var manifestContent = buffer.ToArray();
            lock (_stateLock)
            {
                var tagKey = $"{repository}:{tag}";
                if (EnforceImmutableTags && _manifests.ContainsKey(tagKey))
                {
                    return OciRegistryResult<OciRegistryUnit>.Failed(
                        new(
                            OciRegistryFailureKind.Conflict,
                            "The registry rejected a conflicting update."
                        )
                    );
                }

                ManifestDescriptor = descriptor;
                ManifestContent = manifestContent;
                PushedManifestCount++;
                _manifests[tagKey] = descriptor;
                _publishedManifests[tagKey] = new OciRegistryManifest(descriptor, manifestContent);
            }

            return OciRegistryResult<OciRegistryUnit>.Succeeded(new());
        }

        public async Task<OciRegistryResult<OciRegistryDescriptor>> ResolveManifestAsync(
            Uri registry,
            string repository,
            string reference,
            OciRegistryCredential? credential,
            string credentialPartition,
            CancellationToken cancellationToken
        )
        {
            RequestCalls++;
            if (RequireAuthentication && credential is null)
            {
                return OciRegistryResult<OciRegistryDescriptor>.Failed(
                    new(OciRegistryFailureKind.AuthenticationRequired, "authentication required")
                );
            }

            var initialTagLookup = 0;
            if (
                SynchronizeInitialTagLookups
                && string.Equals(reference, _tag, StringComparison.Ordinal)
            )
            {
                initialTagLookup = Interlocked.Increment(ref _initialTagLookupCount);
                if (initialTagLookup <= 2)
                {
                    if (initialTagLookup == 2)
                    {
                        _initialTagLookups.TrySetResult(true);
                    }

                    await _initialTagLookups.Task.WaitAsync(cancellationToken);
                }
            }

            OciRegistryResult<OciRegistryDescriptor> result;
            lock (_stateLock)
            {
                if (_manifests.TryGetValue($"{repository}:{reference}", out var descriptor))
                {
                    if (
                        CompletionDigestOverride is { } digest
                        && string.Equals(reference, _tag, StringComparison.Ordinal)
                    )
                    {
                        descriptor = descriptor with { Digest = digest };
                    }

                    result = OciRegistryResult<OciRegistryDescriptor>.Succeeded(descriptor);
                }
                else
                {
                    result = OciRegistryResult<OciRegistryDescriptor>.Failed(
                        new(OciRegistryFailureKind.NotFound, "not found")
                    );
                }
            }

            if (initialTagLookup is > 0 and <= 2)
            {
                if (Interlocked.Increment(ref _initialTagLookupReadCount) == 2)
                {
                    _initialTagLookupReads.TrySetResult(true);
                }

                await _initialTagLookupReads.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        private OciRegistryFailure? GetPushFailure(OciRegistryCredential? credential)
        {
            if (RequireAuthentication && credential is null)
            {
                return new(
                    OciRegistryFailureKind.AuthenticationRequired,
                    "authentication required"
                );
            }

            return DenyPush
                ? new(OciRegistryFailureKind.AuthorizationDenied, "permission denied")
                : null;
        }

        private static async Task<OciRegistryResult<OciBlobTransfer>> CopyBlobAsync(
            byte[] content,
            Stream destination,
            CancellationToken cancellationToken
        )
        {
            await destination.WriteAsync(content, cancellationToken);
            return OciRegistryResult<OciBlobTransfer>.Succeeded(
                new OciBlobTransfer(content.LongLength, OciPackArtifact.ComputeDigest(content))
            );
        }
    }
}
