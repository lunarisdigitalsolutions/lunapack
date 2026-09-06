using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Planning;
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
        var resolver = CreateResolver(fileSystem);

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
    public async Task Resolve_WhenReferenceDisablesHooks_PreservesIncomingPolicy()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                "id: application\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\npacks:\n  - id: foundation\n    version: 1.0.0\n    disabledHooks:\n      - preInstall\n      - postInstall\n"
            ),
            (ProjectPath("packs", "foundation", "pack.yml"), CreateFilePack("foundation"))
        );
        var resolver = CreateResolver(fileSystem);

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
        var resolver = CreateResolver(fileSystem);

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
        var resolver = CreateResolver(fileSystem);

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Resolve_WhenGraphContainsCycle_IgnoresClosingEdge()
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
        var resolver = CreateResolver(fileSystem);

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Packs).Count().IsEqualTo(2);
        await Assert
            .That(result.RequireValue().Warnings)
            .IsEquivalentTo([
                "Ignored dependency cycle 'application@1.0.0 -> foundation@1.0.0 -> application@1.0.0'; edge 'foundation@1.0.0 -> application@1.0.0' closes the active path.",
            ]);
        var application = result
            .RequireValue()
            .Packs.Single(pack =>
                string.Equals(pack.Manifest.Id, "application", StringComparison.Ordinal)
            );
        await Assert.That(result.RequireValue().GetIncomingReferences(application)).IsEmpty();
        var selected = result
            .RequireValue()
            .Select(
                new ResolvedPackParameters(
                    new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal),
                    new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
                )
            );
        await Assert.That(selected.IsSuccess).IsTrue();
        await Assert.That(selected.RequireValue().GetIncomingReferences(application)).IsEmpty();
    }

    [Test]
    public async Task Resolve_WhenPackReferencesItself_IgnoresSelfEdge()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "application")
            )
        );
        var resolver = CreateResolver(fileSystem);

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Packs).Count().IsEqualTo(1);
        await Assert
            .That(result.RequireValue().Warnings)
            .IsEquivalentTo(["Ignored self-reference 'application@1.0.0 -> application@1.0.0'."]);
    }

    [Test]
    public async Task Resolve_WhenSiblingInstancesUseDifferentVersions_RetainsBothVersions()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application-one", "pack.yml"),
                CreateFilePack("application", version: "1.0.0")
            ),
            (
                ProjectPath("packs", "application-two", "pack.yml"),
                CreateFilePack("application", version: "2.0.0")
            )
        );
        var resolver = CreateResolver(fileSystem);

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            [
                new ProjectConfiguration.RequestedPack
                {
                    Id = "application",
                    Name = "one",
                    Version = "1.0.0",
                },
                new ProjectConfiguration.RequestedPack
                {
                    Id = "application",
                    Name = "two",
                    Version = "2.0.0",
                },
            ]
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Packs.Select(pack => pack.Manifest.Version))
            .IsEquivalentTo(["1.0.0", "2.0.0"]);
    }

    [Test]
    public async Task Resolve_WhenSiblingInstancesUseSameExactNode_ReusesNode()
    {
        var fileSystem = CreateFileSystem(
            (ProjectPath("packs", "application", "pack.yml"), CreateFilePack("application"))
        );
        var resolver = CreateResolver(fileSystem);

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            [
                new ProjectConfiguration.RequestedPack { Id = "application", Name = "one" },
                new ProjectConfiguration.RequestedPack { Id = "application", Name = "two" },
            ]
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Packs).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Resolve_WhenSiblingInstancesUseDifferentTransitiveVersions_RetainsBothGraphs()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application-one", "pack.yml"),
                CreateCompositePackWithVersions("application", "1.0.0", "shared", "1.0.0")
            ),
            (
                ProjectPath("packs", "application-two", "pack.yml"),
                CreateCompositePackWithVersions("application", "2.0.0", "shared", "2.0.0")
            ),
            (
                ProjectPath("packs", "shared-one", "pack.yml"),
                CreateFilePack("shared", version: "1.0.0")
            ),
            (
                ProjectPath("packs", "shared-two", "pack.yml"),
                CreateFilePack("shared", version: "2.0.0")
            )
        );
        var resolver = CreateResolver(fileSystem);

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            [
                new ProjectConfiguration.RequestedPack
                {
                    Id = "application",
                    Name = "one",
                    Version = "1.0.0",
                },
                new ProjectConfiguration.RequestedPack
                {
                    Id = "application",
                    Name = "two",
                    Version = "2.0.0",
                },
            ]
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(
                result
                    .RequireValue()
                    .Packs.Select(pack => $"{pack.Manifest.Id}@{pack.Manifest.Version}")
            )
            .IsEquivalentTo([
                "application@1.0.0",
                "application@2.0.0",
                "shared@1.0.0",
                "shared@2.0.0",
            ]);
    }

    [Test]
    public async Task Resolve_WhenBranchesShareDependency_RetainsBothNonCyclicEdges()
    {
        var fileSystem = CreateFileSystem(
            (
                ProjectPath("packs", "application", "pack.yml"),
                CreateCompositePack("application", "first", "second")
            ),
            (ProjectPath("packs", "first", "pack.yml"), CreateCompositePack("first", "shared")),
            (ProjectPath("packs", "second", "pack.yml"), CreateCompositePack("second", "shared")),
            (ProjectPath("packs", "shared", "pack.yml"), CreateFilePack("shared"))
        );
        var resolver = CreateResolver(fileSystem);

        var result = await resolver.ResolveAsync(
            _projectDirectory,
            CreateConfiguration("packs"),
            "application",
            null
        );

        await Assert.That(result.IsSuccess).IsTrue();
        var graph = result.RequireValue();
        var shared = graph.Packs.Single(pack =>
            string.Equals(pack.Manifest.Id, "shared", StringComparison.Ordinal)
        );
        await Assert.That(graph.GetIncomingReferences(shared)).Count().IsEqualTo(2);
        await Assert.That(graph.Warnings).IsEmpty();
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
        var resolver = CreateResolver(fileSystem);

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

    private static CompositePackGraphResolver CreateResolver(MockFileSystem fileSystem)
    {
        var console = TestConsole.Create();
        return new CompositePackGraphResolver(new PackCatalog(fileSystem, console), console);
    }

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

    private static string CreateCompositePackWithVersions(
        string id,
        string version,
        string dependencyId,
        string dependencyVersion
    ) =>
        $"id: {id}\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\npacks:\n  - id: {dependencyId}\n    version: {dependencyVersion}\n";

    private static string CreateFilePack(
        string id,
        string? description = null,
        string version = "1.0.0"
    )
    {
        var descriptionLine = description is null ? string.Empty : $"description: {description}\n";

        return $"id: {id}\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\n{descriptionLine}managedFiles:\n  - source: source.txt\n    target: target.txt\n";
    }
}
