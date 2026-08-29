using System.IO.Abstractions.TestingHelpers;
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
        string version = "1.0.0"
    )
    {
        var descriptionLine = description is null ? string.Empty : $"description: {description}\n";

        return $"id: {id}\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\n{descriptionLine}managedFiles:\n  - source: source.txt\n    target: target.txt\n";
    }
}
