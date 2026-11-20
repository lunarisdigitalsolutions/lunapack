using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests.Catalog;

public sealed class CompositePackGraphResolverTests
{
    private static readonly string _projectDirectory = Path.GetFullPath("project");

    [Test]
    public async Task Resolve_WhenCompositeReferencesNested_ResolvesDepthFirst()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "foundation")
            ),
            (
                ProjectPath("packs", "foundation", "pack.yml"),
                CreateCompositePack("foundation", "logging")
            ),
            (ProjectPath("packs", "logging", "pack.yml"), CreateFilePack("logging"))
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsTrue();
        var packs = result.RequireValue().Packs;
        await Assert.That(packs).Count().IsEqualTo(3);
        await Assert.That(packs[0].Manifest.Id).IsEqualTo("logging");
        await Assert.That(packs[1].Manifest.Id).IsEqualTo("foundation");
        await Assert.That(packs[2].Manifest.Id).IsEqualTo("application");
    }

    [Test]
    public async Task Resolve_WhenRepeatedDependencyCatalogWorkExceedsAggregateLimit_Fails()
    {
        const int manifestSizeBytes = 34 * 1024 * 1024;
        var paddedDescription = new string('x', manifestSizeBytes);
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "foundation")
                    + $"\ndescription: {paddedDescription}\n"
            ),
            (ProjectPath("packs", "foundation", "pack.yml"), CreateFilePack("foundation"))
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );
        var limits = ResourceLimits.Default with { PackManifestBytes = 36L * 1024 * 1024 };

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null,
            resourceLimits: limits
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert
            .That(result.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(result.Error).Contains("byte aggregate limit");
    }

    [Test]
    public async Task Resolve_WhenRequestedRootIsDependencyOnly_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "dependency-only", "pack.yml"),
                CreateFilePack("dependency-only", directInstallable: false)
            )
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "dependency-only",
            null
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("not directly installable");
    }

    [Test]
    public async Task Resolve_WhenExplicitVersionIsDependencyOnly_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application-v1", "pack.yml"),
                CreateFilePack("application", version: "1.0.0")
            ),
            (
                ProjectPath("packs", "application-v2", "pack.yml"),
                CreateFilePack("application", version: "2.0.0", directInstallable: false)
            )
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            "2.0.0"
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("not directly installable");
    }

    [Test]
    public async Task Resolve_WhenDependencyIsNotDirectInstallable_StillResolvesComposite()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "dependency-only")
            ),
            (
                ProjectPath("packs", "dependency-only", "pack.yml"),
                CreateFilePack("dependency-only", directInstallable: false)
            )
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Packs.Select(pack => pack.Manifest.Id))
            .IsEquivalentTo(["application", "dependency-only"]);
    }

    [Test]
    public async Task Resolve_WhenNewestRootReleaseIsDependencyOnly_UsesOlderEligibleRelease()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application-v1", "pack.yml"),
                CreateFilePack("application", version: "1.0.0")
            ),
            (
                ProjectPath("packs", "application-v2", "pack.yml"),
                CreateFilePack("application", version: "2.0.0", directInstallable: false)
            )
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Packs.Single().Manifest.Version).IsEqualTo("1.0.0");
    }

    [Test]
    public async Task Resolve_WhenReferenceDisablesHooks_PreservesIncomingPolicy()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                "id: application\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\npacks:\n  - id: foundation\n    version: 1.0.0\n    disabledHooks:\n      - preInstall\n      - postInstall\n"
            ),
            (ProjectPath("packs", "foundation", "pack.yml"), CreateFilePack("foundation"))
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsTrue();
        var graph = result.RequireValue();
        var dependency = graph.Packs.Single(pack =>
            string.Equals(pack.Manifest.Id, "foundation", StringComparison.Ordinal)
        );
        await Assert
            .That(graph.GetIncomingReferences(dependency).Single().DisabledHooks)
            .IsEquivalentTo(["preInstall", "postInstall"]);
    }

    [Test]
    public async Task Resolve_WhenCompositeCandidateExistsInMultipleSources_UsesEarliestSource()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("first", "application", "pack.yml"),
                CreateCompositePack("application", "shared")
            ),
            (ProjectPath("first", "shared", "pack.yml"), CreateFilePack("shared", "first")),
            (ProjectPath("second", "shared", "pack.yml"), CreateFilePack("shared", "second"))
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("first", "second"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsTrue();
        var sharedPack = result.RequireValue().Packs[0];
        await Assert.That(sharedPack.SourcePath).IsEqualTo(ProjectPath("first"));
        await Assert.That(sharedPack.Manifest.Description).IsEqualTo("first");
    }

    [Test]
    public async Task Resolve_WhenReferenceMissing_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "missing")
            )
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Resolve_WhenGraphContainsCycle_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "foundation")
            ),
            (
                ProjectPath("packs", "foundation", "pack.yml"),
                CreateCompositePack("foundation", "application")
            )
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Resolve_WhenGraphMatchesDepthLimit_AcceptsBoundaryAndRejectsLimitPlusOne()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "foundation")
            ),
            (
                ProjectPath("packs", "foundation", "pack.yml"),
                CreateCompositePack("foundation", "logging")
            ),
            (ProjectPath("packs", "logging", "pack.yml"), CreateFilePack("logging"))
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var exact = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null,
            resourceLimits: ResourceLimits.Default with
            {
                DependencyDepth = 2,
            }
        );
        var overLimit = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null,
            resourceLimits: ResourceLimits.Default with
            {
                DependencyDepth = 1,
            }
        );

        await Assert.That(exact.IsSuccess).IsTrue().Because(exact.Error ?? string.Empty);
        await Assert.That(overLimit.IsSuccess).IsFalse();
        await Assert
            .That(overLimit.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(overLimit.Error).Contains("1 edge-depth limit");
        await Assert.That(overLimit.Error).Contains("at least 2 edges observed");
    }

    [Test]
    public async Task Resolve_WhenSharedRootsExceedNodeLimit_RejectsAdditionalRoot()
    {
        var fileSystem = CreateFileSystem(
            (ProjectPath("packs", "first", "pack.yml"), CreateFilePack("first")),
            (ProjectPath("packs", "second", "pack.yml"), CreateFilePack("second"))
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );
        var requestedPacks = new[]
        {
            new ProjectConfiguration.RequestedPack { Id = "first" },
            new ProjectConfiguration.RequestedPack { Id = "second" },
        };

        var exact = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            requestedPacks,
            resourceLimits: ResourceLimits.Default with
            {
                DependencyNodes = 2,
            }
        );
        var overLimit = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            requestedPacks,
            resourceLimits: ResourceLimits.Default with
            {
                DependencyNodes = 1,
            }
        );

        await Assert.That(exact.IsSuccess).IsTrue().Because(exact.Error ?? string.Empty);
        await Assert.That(exact.RequireValue().Packs.Count).IsEqualTo(2);
        await Assert.That(overLimit.IsSuccess).IsFalse();
        await Assert
            .That(overLimit.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(overLimit.Error).Contains("at least 2 unique nodes observed");
    }

    [Test]
    public async Task Resolve_WhenDuplicateEdgeExceedsReferenceLimit_CountsEachTraversal()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "shared", "shared")
            ),
            (ProjectPath("packs", "shared", "pack.yml"), CreateFilePack("shared"))
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var exact = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null,
            resourceLimits: ResourceLimits.Default with
            {
                DependencyReferences = 2,
            }
        );
        var overLimit = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null,
            resourceLimits: ResourceLimits.Default with
            {
                DependencyReferences = 1,
            }
        );

        await Assert.That(exact.IsSuccess).IsTrue().Because(exact.Error ?? string.Empty);
        await Assert.That(overLimit.IsSuccess).IsFalse();
        await Assert
            .That(overLimit.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(overLimit.Error).Contains("1 reference limit");
        await Assert.That(overLimit.Error).Contains("at least 2 references traversed");
    }

    [Test]
    public async Task Resolve_WhenCycleExceedsReferenceLimit_ReportsResourceBeforeRecursing()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "foundation")
            ),
            (
                ProjectPath("packs", "foundation", "pack.yml"),
                CreateCompositePack("foundation", "application")
            )
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null,
            resourceLimits: ResourceLimits.Default with
            {
                DependencyReferences = 1,
            }
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert
            .That(result.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(result.Error).Contains("at least 2 references traversed");
    }

    [Test]
    public async Task Resolve_WhenSharedDagHasDeeperPath_ValidatesMaximumDepth()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "shared", "chain")
            ),
            (ProjectPath("packs", "shared", "pack.yml"), CreateCompositePack("shared", "leaf")),
            (ProjectPath("packs", "chain", "pack.yml"), CreateCompositePack("chain", "shared")),
            (ProjectPath("packs", "leaf", "pack.yml"), CreateFilePack("leaf"))
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null,
            resourceLimits: ResourceLimits.Default with
            {
                DependencyDepth = 2,
            }
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert
            .That(result.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(result.Error).Contains("at least 3 edges observed");
    }

    [Test]
    public async Task Resolve_WhenOneIdHasConflictingVersions_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "first", "other")
            ),
            (ProjectPath("packs", "first", "pack.yml"), CreateCompositePack("first", "shared")),
            (
                ProjectPath("packs", "other", "pack.yml"),
                CreateCompositePackWithVersion("other", "shared", "2.0.0")
            ),
            (
                ProjectPath("packs", "shared-one", "pack.yml"),
                CreateFilePack("shared", "one", "1.0.0")
            ),
            (
                ProjectPath("packs", "shared-two", "pack.yml"),
                CreateFilePack("shared", "two", "2.0.0")
            )
        );
        var resolver = new CompositePackGraphResolver(
            new PackCatalog(fileSystem, TestConsole.Create())
        );

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    private static ProjectConfiguration CreateConfiguration(params string[] sourcePaths) =>
        new()
        {
            SchemaVersion = 1,
            Sources =
            [
                .. sourcePaths.Select(
                    (path, index) =>
                        new ProjectConfiguration.LocalSource
                        {
                            Name = $"source-{index}",
                            Path = path,
                        }
                ),
            ],
        };

    private static MockFileSystem CreateFileSystem(params (string Path, string Contents)[] files)
    {
        var fileSystem = new MockFileSystem();

        foreach (var file in files)
        {
            var packDirectory = fileSystem.Path.GetDirectoryName(file.Path).RequireNotNull();
            fileSystem.Directory.CreateDirectory(packDirectory);
            fileSystem.File.WriteAllText(file.Path, file.Contents);
            if (file.Contents.Contains("- source: source.txt", StringComparison.Ordinal))
            {
                fileSystem.File.WriteAllText(
                    fileSystem.Path.Combine(packDirectory, "source.txt"),
                    "source"
                );
            }
        }

        return fileSystem;
    }

    private static string ProjectPath(params string[] paths) =>
        Path.Combine([_projectDirectory, .. paths]);

    private static string CreateCompositePack(string id, params string[] dependencyIds)
    {
        var packReferences = string.Join(
            string.Empty,
            dependencyIds.Select(dependencyId => $"  - id: {dependencyId}\n    version: 1.0.0\n")
        );

        return $"id: {id}\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\npacks:\n{packReferences}";
    }

    private static string CreateCompositePackWithVersion(
        string id,
        string dependencyId,
        string dependencyVersion
    ) =>
        $"id: {id}\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\npacks:\n  - id: {dependencyId}\n    version: {dependencyVersion}\n";

    private static string CreateFilePack(
        string id,
        string? description = null,
        string version = "1.0.0",
        bool directInstallable = true
    )
    {
        var descriptionLine = description is null ? string.Empty : $"description: {description}\n";
        var installableLine = directInstallable ? string.Empty : "directInstallable: false\n";

        return $"id: {id}\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\n{installableLine}{descriptionLine}managedFiles:\n  - source: source.txt\n    target: target.txt\n";
    }
}
