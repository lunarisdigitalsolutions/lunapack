using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests;

public sealed class PackUpdatePlannerTests
{
    private static readonly string _projectDirectory = Path.GetFullPath("project");
    private static readonly string _packsDirectory = Path.GetFullPath("packs");

    [Test]
    public async Task Plan_WhenManagedTargetAdded_CreatesActionWithResultingHash()
    {
        var managedFile = CreateManagedFile("example", "target.txt", "new content");
        var planner = new PackUpdatePlanner(new MockFileSystem());

        var result = planner.Plan(
            _projectDirectory,
            new ProjectLockFile { SchemaVersion = 1 },
            new PackInstallationPlan([managedFile])
        );

        var action = result.RequireValue().Actions.Single();
        await Assert.That(action).IsTypeOf<CreateManagedFileUpdateAction>();
        await Assert.That(action.ResultingSha256).IsEqualTo(ComputeSha256("new content"));
    }

    [Test]
    public async Task Plan_WhenMergeTargetsShareAbsentTarget_ChainsResultingContents()
    {
        var firstManagedFile = CreateManagedFile(
            "first",
            ".gitignore",
            "# first:start\nfirst\n# first:end\n",
            new PackManifest.PackManagedFileStrategy { Type = "merge", Method = "section" }
        );
        var secondManagedFile = CreateManagedFile(
            "second",
            ".gitignore",
            "# second:start\nsecond\n# second:end\n",
            new PackManifest.PackManagedFileStrategy { Type = "merge", Method = "section" }
        );
        var planner = new PackUpdatePlanner(new MockFileSystem());

        var result = planner.Plan(
            _projectDirectory,
            new ProjectLockFile { SchemaVersion = 1 },
            new PackInstallationPlan([firstManagedFile, secondManagedFile])
        );

        var actions = result.RequireValue().Actions;
        await Assert.That(actions).Count().IsEqualTo(2);
        await Assert.That(actions[0]).IsTypeOf<CreateManagedFileUpdateAction>();
        await Assert.That(actions[1]).IsTypeOf<MergeSectionManagedFileUpdateAction>();
        await Assert
            .That(ReadContents(actions[1]))
            .IsEqualTo("# first:start\nfirst\n# first:end\n# second:start\nsecond\n# second:end\n");
    }

    [Test]
    public async Task Plan_WhenOwnedTargetContentChanged_CreatesCopyAction()
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), "old content"));
        var managedFile = CreateManagedFile("example", "target.txt", "new content");
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([managedFile])
        );

        var action = result.RequireValue().Actions.Single();
        await Assert.That(action).IsTypeOf<CopyManagedFileUpdateAction>();
        await Assert.That(action.ResultingSha256).IsEqualTo(ComputeSha256("new content"));
    }

    [Test]
    public async Task Plan_WhenTargetHashDrifts_AppliesDeclaredCopyStrategy()
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), "user change"));
        var managedFile = CreateManagedFile("example", "target.txt", "new content");
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([managedFile])
        );

        var action = result.RequireValue().Actions.Single();
        await Assert.That(action).IsTypeOf<CopyManagedFileUpdateAction>();
        await Assert.That(action.ResultingSha256).IsEqualTo(ComputeSha256("new content"));
    }

    [Test]
    public async Task Plan_WhenDeclaredTargetMatchesLock_RetainsLockedEffectiveTarget()
    {
        const string declaredTarget = "docs/adr/template.md";
        const string lockedTarget = "docs/internal/adr/template.md";
        var fileSystem = CreateFileSystem((ProjectPath(lockedTarget), "old content"));
        var managedFile = CreateManagedFile(
            "example",
            "docs/architecture/decisions/template.md",
            "new content",
            declaredTarget: declaredTarget
        );
        var lockFile = CreateLockFile("example", lockedTarget, "old content", declaredTarget);
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            lockFile,
            new PackInstallationPlan([managedFile])
        );

        var action = result.RequireValue().Actions.Single();
        await Assert.That(action).IsTypeOf<CopyManagedFileUpdateAction>();
        await Assert.That(action.TargetPathRelativeToProject).IsEqualTo(lockedTarget);
    }

    [Test]
    public async Task Plan_WhenTargetExistsAndCopyFails_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), "existing content"));
        var managedFile = CreateManagedFile(
            "example",
            "target.txt",
            "new content",
            new PackManifest.PackManagedFileStrategy { Type = "copy", Method = "fail-if-exists" }
        );
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([managedFile])
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Plan_WhenTargetExistsAndCopySkipped_PreservesTargetHash()
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), "existing content"));
        var managedFile = CreateManagedFile(
            "example",
            "target.txt",
            "new content",
            new PackManifest.PackManagedFileStrategy { Type = "copy", Method = "skip-if-exists" }
        );
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([managedFile])
        );

        var action = result.RequireValue().Actions.Single();
        await Assert.That(action).IsTypeOf<SkipManagedFileUpdateAction>();
        await Assert.That(action.ResultingSha256).IsEqualTo(ComputeSha256("existing content"));
    }

    [Test]
    public async Task Plan_WhenTargetExistsAndCopiedWithBackup_UsesNextNumericPath()
    {
        var fileSystem = CreateFileSystem(
            (ProjectPath("target.txt"), "existing content"),
            (ProjectPath("target.txt.1"), "first backup")
        );
        var managedFile = CreateManagedFile(
            "example",
            "target.txt",
            "new content",
            new PackManifest.PackManagedFileStrategy
            {
                Type = "copy",
                Method = "backup-and-overwrite",
            }
        );
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([managedFile])
        );

        var action = result.RequireValue().Actions.Single();
        await Assert.That(action).IsTypeOf<BackupAndCopyManagedFileUpdateAction>();
        await Assert
            .That(((BackupAndCopyManagedFileUpdateAction)action).BackupPath)
            .IsEqualTo(ProjectPath("target.txt.2"));
    }

    [Test]
    public async Task Plan_WhenLinesMerged_AppendsOnlyMissingSourceLines()
    {
        var action = PlanMerge("alpha\nbeta\n", "beta\ngamma\n", "lines");

        await Assert.That(action).IsTypeOf<MergeLinesManagedFileUpdateAction>();
        await Assert.That(ReadContents(action)).IsEqualTo("alpha\nbeta\ngamma\n");
    }

    [Test]
    public async Task Plan_WhenSectionMarkersPresent_ReplacesMarkedSection()
    {
        var action = PlanMerge(
            "before\n# start\nold\n# end\nafter\n",
            "# start\nnew\n# end\n",
            "section"
        );

        await Assert.That(action).IsTypeOf<MergeSectionManagedFileUpdateAction>();
        await Assert.That(ReadContents(action)).IsEqualTo("before\n# start\nnew\n# end\nafter\n");
    }

    [Test]
    public async Task Plan_WhenSectionMarkersAbsent_AppendsSourceSection()
    {
        var action = PlanMerge("before\n", "# start\nnew\n# end\n", "section");

        await Assert.That(action).IsTypeOf<MergeSectionManagedFileUpdateAction>();
        await Assert.That(ReadContents(action)).IsEqualTo("before\n# start\nnew\n# end\n");
    }

    [Test]
    public async Task Plan_WhenSectionMarkersIncomplete_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), "# start\nold\n"));
        var managedFile = CreateManagedFile(
            "example",
            "target.txt",
            "# start\nnew\n# end\n",
            new PackManifest.PackManagedFileStrategy { Type = "merge", Method = "section" }
        );
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([managedFile])
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Plan_WhenJsonObjectsMerged_PreservesDestinationProperties()
    {
        var action = PlanMerge(
            "{\"keep\":true,\"nested\":{\"old\":1,\"shared\":\"old\"}}",
            "{\"add\":true,\"nested\":{\"shared\":\"new\"}}",
            "json"
        );

        await Assert.That(action).IsTypeOf<MergeJsonManagedFileUpdateAction>();
        await Assert
            .That(
                JsonNode.DeepEquals(
                    JsonNode.Parse(ReadContents(action)),
                    JsonNode.Parse(
                        "{\"keep\":true,\"nested\":{\"old\":1,\"shared\":\"new\"},\"add\":true}"
                    )
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task Plan_WhenJsonArraysMerged_PreservesOrderAndExcludesDuplicates()
    {
        var action = PlanMerge("[1,{\"name\":\"one\"}]", "[{\"name\":\"one\"},2]", "json");

        await Assert.That(action).IsTypeOf<MergeJsonManagedFileUpdateAction>();
        await Assert
            .That(
                JsonNode.DeepEquals(
                    JsonNode.Parse(ReadContents(action)),
                    JsonNode.Parse("[1,{\"name\":\"one\"},2]")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task Plan_WhenJsonKindsDiffer_ReturnsFailure()
    {
        var result = PlanMergeResult("{\"existing\":true}", "[\"source\"]", "json");

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Plan_WhenJsonSourceMalformed_ReturnsFailure()
    {
        var result = PlanMergeResult("{\"existing\":true}", "{invalid", "json");

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Plan_WhenManagedTargetRemoved_CreatesDeleteAction()
    {
        var planner = new PackUpdatePlanner(new MockFileSystem());

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([])
        );

        var action = result.RequireValue().Actions.Single();
        await Assert.That(action).IsTypeOf<DeleteManagedFileUpdateAction>();
        await Assert.That(action.ResultingSha256).IsNull();
    }

    [Test]
    public async Task Plan_WhenManagedTargetIgnored_DoesNotCreateDeleteAction()
    {
        var planner = new PackUpdatePlanner(new MockFileSystem());

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([])
            {
                IgnoredDeclaredTargets = new HashSet<string>(StringComparer.Ordinal)
                {
                    "target.txt",
                },
            }
        );

        await Assert.That(result.RequireValue().Actions).IsEmpty();
    }

    private static PlannedManagedFile CreateManagedFile(
        string packId,
        string target,
        string contents,
        PackManifest.PackManagedFileStrategy? strategy = null,
        string? declaredTarget = null
    ) =>
        new(
            new DiscoveredPack(
                _packsDirectory,
                PacksPath("example"),
                new PackManifest
                {
                    Id = packId,
                    Version = "2.0.0",
                    ManagedFiles = [],
                }
            ),
            PacksPath("example", "source.txt"),
            declaredTarget ?? target,
            Encoding.UTF8.GetBytes(contents),
            ProjectPath(target),
            target,
            strategy ?? PackManifest.PackManagedFileStrategy.CopyOverwrite
        );

    private static MockFileSystem CreateFileSystem(params (string Path, string Contents)[] files)
    {
        var fileSystem = new MockFileSystem();
        foreach (var file in files)
        {
            fileSystem.Directory.CreateDirectory(
                fileSystem.Path.GetDirectoryName(file.Path).RequireNotNull()
            );
            fileSystem.File.WriteAllText(file.Path, file.Contents);
        }

        return fileSystem;
    }

    private static string ProjectPath(params string[] paths) =>
        Path.Combine([_projectDirectory, .. paths]);

    private static string PacksPath(params string[] paths) =>
        Path.Combine([_packsDirectory, .. paths]);

    private static PlannedPackUpdateAction PlanMerge(
        string targetContents,
        string sourceContents,
        string method
    ) => PlanMergeResult(targetContents, sourceContents, method).RequireValue().Actions.Single();

    private static ManifestOperationResult<PackUpdatePlan> PlanMergeResult(
        string targetContents,
        string sourceContents,
        string method
    )
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), targetContents));
        var managedFile = CreateManagedFile(
            "example",
            "target.txt",
            sourceContents,
            new PackManifest.PackManagedFileStrategy { Type = "merge", Method = method }
        );
        var planner = new PackUpdatePlanner(fileSystem);

        return planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([managedFile])
        );
    }

    private static string ReadContents(PlannedPackUpdateAction action)
    {
        var contents = action.ResultingContents;
        return contents is null
            ? throw new InvalidOperationException("Merge action has no resulting contents.")
            : Encoding.UTF8.GetString(contents);
    }

    private static ProjectLockFile CreateLockFile(
        string packId,
        string target,
        string contents,
        string? declaredTarget = null
    ) =>
        new()
        {
            SchemaVersion = declaredTarget is null ? 1 : 2,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = packId,
                    Version = "1.0.0",
                    SourcePath = "packs",
                    PackPath = "example",
                    ManagedFiles =
                    [
                        new ProjectLockFile.ManagedFile
                        {
                            DeclaredTargetPath = declaredTarget,
                            TargetPath = target,
                            Sha256 = ComputeSha256(contents),
                        },
                    ],
                },
            ],
        };

    private static string ComputeSha256(string contents) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents)));
}
