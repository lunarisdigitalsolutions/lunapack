using System.IO.Abstractions.TestingHelpers;

namespace Lunapack.Cli.UnitTests;

public sealed class PackInstallationPlannerTests
{
    private static readonly string _projectDirectory = Path.GetFullPath("project");
    private static readonly string _packsDirectory = Path.GetFullPath("packs");
    private static readonly ResolvedPackParameters _emptyParameters = new(
        new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal),
        new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
    );

    [Test]
    public async Task Plan_WhenTargetClaimedTwiceWithCopyStrategy_ReturnsPreciseFailure()
    {
        var fileSystem = CreateFileSystem(
            (PacksPath("one", "source.txt"), "one"),
            (PacksPath("two", "source.txt"), "two")
        );
        var planner = CreatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            new ResolvedPackGraph([
                CreatePack("one", PacksPath("one"), "shared.txt"),
                CreatePack("two", PacksPath("two"), "shared.txt"),
            ]),
            new ProjectLockFile { SchemaVersion = 1 },
            new ProjectConfiguration { SchemaVersion = 1 },
            new PackInstallationRequest(new PackReference("one", null), null, false),
            _emptyParameters
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert
            .That(result.Error)
            .IsEqualTo("Target 'shared.txt' is claimed by both 'one' and 'two'.");
    }

    [Test]
    public async Task Plan_WhenTemplateMissing_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem();
        var planner = CreatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            new ResolvedPackGraph([CreatePack("one", PacksPath("one"), "target.txt")]),
            new ProjectLockFile { SchemaVersion = 1 },
            new ProjectConfiguration { SchemaVersion = 1 },
            new PackInstallationRequest(new PackReference("one", null), null, false),
            _emptyParameters
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Plan_WhenConditionFalse_ExcludesManagedFileBeforeSourceValidation()
    {
        var fileSystem = CreateFileSystem();
        var planner = CreatePlanner(fileSystem);
        var parameters = new ResolvedPackParameters(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal)
            {
                ["includeCi"] = new(PackParameterType.Bool, false, []),
            },
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
            {
                ["includeCi"] = new(PackParameterType.Bool, string.Empty, false),
            }
        );

        var result = planner.Plan(
            _projectDirectory,
            new ResolvedPackGraph([CreatePack("one", PacksPath("one"), "target.txt", "includeCi")]),
            new ProjectLockFile { SchemaVersion = 1 },
            new ProjectConfiguration { SchemaVersion = 1 },
            new PackInstallationRequest(new PackReference("one", null), null, false),
            parameters
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().ManagedFiles).IsEmpty();
    }

    [Test]
    public async Task Plan_WhenTargetExistsUnowned_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem(
            (PacksPath("one", "source.txt"), "template"),
            (ProjectPath("target.txt"), "user content")
        );
        var planner = CreatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            new ResolvedPackGraph([CreatePack("one", PacksPath("one"), "target.txt")]),
            new ProjectLockFile { SchemaVersion = 1 },
            new ProjectConfiguration { SchemaVersion = 1 },
            new PackInstallationRequest(new PackReference("one", null), null, false),
            _emptyParameters
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Plan_WhenUpdateTargetOwnedByPriorVersion_AllowsStrategyPlanning()
    {
        var fileSystem = CreateFileSystem(
            (PacksPath("one", "source.txt"), "template"),
            (ProjectPath("target.txt"), "existing content")
        );
        var planner = CreatePlanner(fileSystem);
        var lockFile = new ProjectLockFile
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "one",
                    Version = "1.0.0",
                    SourcePath = "packs",
                    PackPath = "one",
                    ManagedFiles =
                    [
                        new ProjectLockFile.ManagedFile
                        {
                            TargetPath = "target.txt",
                            Sha256 = "unused",
                        },
                    ],
                },
            ],
        };

        var result = planner.Plan(
            _projectDirectory,
            new ResolvedPackGraph([
                CreatePack("one", PacksPath("one"), "target.txt", version: "2.0.0"),
            ]),
            lockFile,
            new ProjectConfiguration { SchemaVersion = 1 },
            new PackInstallationRequest(new PackReference("one", "2.0.0"), null, false)
            {
                PlanningMode = PackManagedFilePlanningMode.Update,
            },
            _emptyParameters
        );

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Plan_WhenGlobalDirectoryRemappingMatches_UsesRemappedEffectiveTarget()
    {
        var fileSystem = CreateFileSystem((PacksPath("one", "source.txt"), "template"));
        var planner = CreatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            new ResolvedPackGraph([CreatePack("one", PacksPath("one"), "docs/adr/template.md")]),
            new ProjectLockFile { SchemaVersion = 1 },
            new ProjectConfiguration
            {
                SchemaVersion = 1,
                Remap = new ProjectConfiguration.Remapping
                {
                    Directories = { ["docs/adr"] = "docs/internal/01-architecture/decisions" },
                },
            },
            new PackInstallationRequest(new PackReference("one", null), null, false),
            _emptyParameters
        );

        var plannedFile = result.RequireValue().ManagedFiles.Single();
        await Assert.That(plannedFile.DeclaredTargetPath).IsEqualTo("docs/adr/template.md");
        await Assert
            .That(plannedFile.TargetPathRelativeToProject)
            .IsEqualTo("docs/internal/01-architecture/decisions/template.md");
    }

    [Test]
    [Arguments("file", "configuration", "file")]
    [Arguments("file", "invocation", "directory")]
    [Arguments("directory", "configuration", "directory")]
    [Arguments("directory", "invocation", "file")]
    [Arguments("glob", "configuration", "file")]
    [Arguments("glob", "invocation", "directory")]
    public async Task Plan_WhenManagedTargetRemapped_ResolvesConcreteTarget(
        string selectorKind,
        string mappingSource,
        string mappingKind
    )
    {
        var fileSystem = CreateFileSystem(
            (PacksPath("one", "content", "nested", "guide.txt"), "guide")
        );
        var planner = CreatePlanner(fileSystem);
        var isFileSelector = string.Equals(selectorKind, "file", StringComparison.Ordinal);
        var declaredTarget = isFileSelector
            ? "docs/development/guide.txt"
            : "docs/development/nested/guide.txt";
        var source = string.Equals(mappingKind, "file", StringComparison.Ordinal)
            ? declaredTarget
            : "docs/development";
        var directories = string.Equals(mappingKind, "directory", StringComparison.Ordinal)
            ? new[] { $"{source.Replace('/', '\\')}=docs\\04-development\\" }
            : [];
        var files = string.Equals(mappingKind, "file", StringComparison.Ordinal)
            ? new[] { $"{source.Replace('/', '\\')}=docs\\04-development\\guide.txt" }
            : [];
        var invocationRemapping = ManagedFileTargetRemapping
            .Create(fileSystem, _projectDirectory, directories, files)
            .RequireValue();
        var configurationRemapping = new ProjectConfiguration.Remapping();
        var target = string.Equals(mappingKind, "file", StringComparison.Ordinal)
            ? "docs/04-development/guide.txt"
            : "docs/04-development";
        var configuredMappings = string.Equals(mappingKind, "file", StringComparison.Ordinal)
            ? configurationRemapping.Files
            : configurationRemapping.Directories;
        configuredMappings[source] = target;
        var pack = new DiscoveredPack(
            _packsDirectory,
            PacksPath("one"),
            new PackManifest
            {
                Id = "one",
                Version = "1.0.0",
                ManagedFiles = [CreateSelectorManagedFile(selectorKind)],
            }
        );
        var usesInvocation = string.Equals(mappingSource, "invocation", StringComparison.Ordinal);

        var result = planner.Plan(
            _projectDirectory,
            new ResolvedPackGraph([pack]),
            new ProjectLockFile { SchemaVersion = 1 },
            new ProjectConfiguration
            {
                SchemaVersion = 1,
                Remap = usesInvocation ? null : configurationRemapping,
            },
            new PackInstallationRequest(new PackReference("one", null), null, false)
            {
                TargetRemapping = usesInvocation ? invocationRemapping : null,
            },
            _emptyParameters
        );

        var plannedFile = result.RequireValue().ManagedFiles.Single();
        var expectedTarget =
            string.Equals(mappingKind, "file", StringComparison.Ordinal)
                ? "docs/04-development/guide.txt"
            : isFileSelector ? "docs/04-development/guide.txt"
            : "docs/04-development/nested/guide.txt";
        await Assert.That(plannedFile.DeclaredTargetPath).IsEqualTo(declaredTarget);
        await Assert.That(plannedFile.TargetPathRelativeToProject).IsEqualTo(expectedTarget);
    }

    [Test]
    public async Task Plan_WhenInvocationFileRemappingMatches_OverridesGlobalDirectoryRemapping()
    {
        var fileSystem = CreateFileSystem((PacksPath("one", "source.txt"), "template"));
        var planner = CreatePlanner(fileSystem);
        var invocationRemapping = ManagedFileTargetRemapping
            .Create(
                fileSystem,
                _projectDirectory,
                [],
                ["docs/adr/template.md=docs/adr/_template.md"]
            )
            .RequireValue();

        var result = planner.Plan(
            _projectDirectory,
            new ResolvedPackGraph([CreatePack("one", PacksPath("one"), "docs/adr/template.md")]),
            new ProjectLockFile { SchemaVersion = 1 },
            new ProjectConfiguration
            {
                SchemaVersion = 1,
                Remap = new ProjectConfiguration.Remapping
                {
                    Directories = { ["docs/adr"] = "docs/architecture" },
                },
            },
            new PackInstallationRequest(new PackReference("one", null), null, false)
            {
                TargetRemapping = invocationRemapping,
            },
            _emptyParameters
        );

        await Assert
            .That(result.RequireValue().ManagedFiles.Single().TargetPathRelativeToProject)
            .IsEqualTo("docs/adr/_template.md");
    }

    [Test]
    public async Task Plan_WhenConfiguredDirectoryMapsToIgnore_OmitsMatchingManagedFiles()
    {
        var fileSystem = CreateFileSystem(
            (PacksPath("one", "content", "nested", "guide.txt"), "guide")
        );
        var planner = CreatePlanner(fileSystem);
        var pack = new DiscoveredPack(
            _packsDirectory,
            PacksPath("one"),
            new PackManifest
            {
                Id = "one",
                Version = "1.0.0",
                ManagedFiles = [CreateSelectorManagedFile("directory")],
            }
        );

        var result = planner.Plan(
            _projectDirectory,
            new ResolvedPackGraph([pack]),
            new ProjectLockFile { SchemaVersion = 1 },
            new ProjectConfiguration
            {
                SchemaVersion = 1,
                Remap = new ProjectConfiguration.Remapping
                {
                    Directories = { ["docs/development"] = "@ignore" },
                },
            },
            new PackInstallationRequest(new PackReference("one", null), null, false),
            _emptyParameters
        );

        await Assert.That(result.RequireValue().ManagedFiles).IsEmpty();
    }

    private static DiscoveredPack CreatePack(
        string id,
        string packDirectory,
        string target,
        string? condition = null,
        string version = "1.0.0"
    ) =>
        new(
            _packsDirectory,
            packDirectory,
            new PackManifest
            {
                Id = id,
                Version = version,
                ManagedFiles =
                [
                    new PackManifest.PackManagedFile
                    {
                        Condition = condition,
                        Source = "source.txt",
                        Target = target,
                    },
                ],
            }
        );

    private static PackManifest.PackManagedFile CreateSelectorManagedFile(string selectorKind) =>
        selectorKind switch
        {
            "file" => new PackManifest.PackManagedFile
            {
                Source = "content/nested/guide.txt",
                Target = "docs/development/guide.txt",
            },
            "directory" => new PackManifest.PackManagedFile
            {
                Directory = "content",
                Target = "docs/development/",
            },
            _ => new PackManifest.PackManagedFile
            {
                Glob = "content/**/*.txt",
                Target = "docs/development/",
            },
        };

    private static MockFileSystem CreateFileSystem(params (string Path, string Contents)[] files)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(_projectDirectory);

        foreach (var file in files)
        {
            fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(file.Path)!);
            fileSystem.File.WriteAllText(file.Path, file.Contents);
        }

        return fileSystem;
    }

    private static string ProjectPath(params string[] paths) =>
        Path.Combine([_projectDirectory, .. paths]);

    private static string PacksPath(params string[] paths) =>
        Path.Combine([_packsDirectory, .. paths]);

    private static PackInstallationPlanner CreatePlanner(MockFileSystem fileSystem) =>
        new(fileSystem, new PackTemplateRenderer(fileSystem), new ManagedFileConditionParser());
}
