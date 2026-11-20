using System.IO.Abstractions;
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

namespace Lunapack.Cli.UnitTests.Packs.Planning;

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
            {
                Remappings =
                [
                    new ManagedFileRemapping(
                        "example",
                        declaredTarget,
                        "docs/architecture/decisions/template.md",
                        ManagedFileRemappingOrigin.Pack
                    ),
                ],
            }
        );

        var action = result.RequireValue().Actions.Single();
        var remapping = result.RequireValue().Remappings.Single();
        await Assert.That(action).IsTypeOf<CopyManagedFileUpdateAction>();
        await Assert.That(action.TargetPathRelativeToProject).IsEqualTo(lockedTarget);
        await Assert.That(remapping.EffectiveTarget).IsEqualTo(lockedTarget);
        await Assert.That(remapping.Origin).IsEqualTo(ManagedFileRemappingOrigin.Lock);
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
    public async Task Plan_WhenJsonObjectsMerged_UsesReadableFormatting()
    {
        var action = PlanMerge("{\"existing\":true}", "{\"added\":1}", "json");

        await Assert
            .That(ReadContents(action))
            .IsEqualTo(
                string.Join(
                    Environment.NewLine,
                    "{",
                    "  \"existing\": true,",
                    "  \"added\": 1",
                    "}"
                )
            );
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
    public async Task Plan_WhenManagedTargetRemovedAndUnchanged_CreatesDeleteAction()
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), "old content"));
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([])
        );

        var action = result.RequireValue().Actions.Single();
        await Assert.That(action).IsTypeOf<DeleteManagedFileUpdateAction>();
        await Assert.That(action.ResultingSha256).IsNull();
        await Assert
            .That(((DeleteManagedFileUpdateAction)action).PlannedTargetSha256)
            .IsEqualTo(ComputeSha256("old content"));
    }

    [Test]
    public async Task Plan_WhenObsoleteManagedTargetModified_PreservesAction()
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), "user content"));
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([])
        );

        await Assert
            .That(result.RequireValue().Actions.Single())
            .IsTypeOf<PreserveManagedFileUpdateAction>();
    }

    [Test]
    public async Task Plan_WhenModifiedRemovalForced_CreatesModifiedDeleteAction()
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), "user content"));
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([]),
            removeModified: true
        );

        var action = result.RequireValue().Actions.Single();
        await Assert.That(action).IsTypeOf<DeleteManagedFileUpdateAction>();
        var deleteAction = (DeleteManagedFileUpdateAction)action;
        await Assert.That(deleteAction.WasModifiedAtPlanning).IsTrue();
        await Assert
            .That(deleteAction.PlannedTargetSha256)
            .IsEqualTo(ComputeSha256("user content"));
    }

    [Test]
    public async Task Plan_WhenObsoleteManagedTargetMissing_CreatesNoAction()
    {
        var planner = new PackUpdatePlanner(new MockFileSystem());

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([])
        );

        await Assert.That(result.RequireValue().Actions).IsEmpty();
    }

    [Test]
    public async Task Plan_WhenSharedObsoleteTargetModified_CreatesOnePreserveAction()
    {
        var fileSystem = CreateFileSystem((ProjectPath("shared.txt"), "user content"));
        var lockFile = CreateSharedLockFile("first", "second", "old content");
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(_projectDirectory, lockFile, new PackInstallationPlan([]));

        await Assert.That(result.RequireValue().Actions).Count().IsEqualTo(1);
        await Assert
            .That(result.RequireValue().Actions.Single())
            .IsTypeOf<PreserveManagedFileUpdateAction>();
    }

    [Test]
    public async Task Plan_WhenSharedTargetRetainedByRemainingOwner_CreatesNoRemovalAction()
    {
        var fileSystem = CreateFileSystem((ProjectPath("shared.txt"), "old content"));
        var lockFile = CreateSharedLockFile("first", "second", "old content");
        var remainingFile = CreateManagedFile("second", "shared.txt", "old content");
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            lockFile,
            new PackInstallationPlan([remainingFile])
        );

        await Assert.That(result.RequireValue().Actions).IsEmpty();
    }

    [Test]
    public async Task Plan_WhenPlannedTargetAliasesPreviousPath_DoesNotCreateRemoval()
    {
        var fileSystem = CreateFileSystem((ProjectPath("shared.txt"), "old content"));
        var lockFile = CreateLockFile("first", "shared.txt", "old content");
        var aliasedTarget = CreateManagedFile("second", "directory/../shared.txt", "new content");
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            lockFile,
            new PackInstallationPlan([aliasedTarget])
        );

        await Assert.That(result.RequireValue().Actions).Count().IsEqualTo(1);
        await Assert
            .That(result.RequireValue().Actions.Single())
            .IsTypeOf<CopyManagedFileUpdateAction>();
    }

    [Test]
    public async Task Plan_WhenPlannedTargetDiffersOnlyByCaseOnCaseInsensitiveHost_DoesNotCreateRemoval()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            Skip.Test("The default filesystem is not known to alias path case.");
        }

        if (OperatingSystem.IsMacOS() && !HostAliasesPathCase())
        {
            Skip.Test("The macOS filesystem is case-sensitive.");
        }

        if (OperatingSystem.IsMacOS())
        {
            var projectDirectory = Directory.CreateTempSubdirectory(
                "lunapack-case-insensitive-project"
            );
            try
            {
                var macFileSystem = new FileSystem();
                await File.WriteAllTextAsync(
                    Path.Combine(projectDirectory.FullName, "Shared.txt"),
                    "old content"
                );
                var macLockFile = CreateLockFile("first", "Shared.txt", "old content");
                var macAliasedTarget = CreateManagedFile(
                    "second",
                    "shared.txt",
                    "new content",
                    projectDirectory: projectDirectory.FullName
                );
                var macPlanner = new PackUpdatePlanner(macFileSystem);

                var macResult = macPlanner.Plan(
                    projectDirectory.FullName,
                    macLockFile,
                    new PackInstallationPlan([macAliasedTarget])
                );

                await Assert.That(macResult.RequireValue().Actions).Count().IsEqualTo(1);
                await Assert
                    .That(macResult.RequireValue().Actions.Single())
                    .IsTypeOf<CopyManagedFileUpdateAction>();
            }
            finally
            {
                projectDirectory.Delete(recursive: true);
            }

            return;
        }

        var fileSystem = CreateFileSystem((ProjectPath("Shared.txt"), "old content"));
        var lockFile = CreateLockFile("first", "Shared.txt", "old content");
        var aliasedTarget = CreateManagedFile("second", "shared.txt", "new content");
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            lockFile,
            new PackInstallationPlan([aliasedTarget])
        );

        await Assert.That(result.RequireValue().Actions).Count().IsEqualTo(1);
        await Assert
            .That(result.RequireValue().Actions.Single())
            .IsTypeOf<CopyManagedFileUpdateAction>();
    }

    [Test]
    public async Task Plan_WhenSharedOwnersHaveInconsistentDigests_PreservesTarget()
    {
        var fileSystem = CreateFileSystem((ProjectPath("shared.txt"), "old content"));
        var lockFile = CreateSharedLockFile("first", "second", "old content");
        lockFile.Packs[1].ManagedFiles[0].Sha256 = ComputeSha256("other locked content");
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(_projectDirectory, lockFile, new PackInstallationPlan([]));

        await Assert
            .That(result.RequireValue().Actions.Single())
            .IsTypeOf<PreserveManagedFileUpdateAction>();
    }

    [Test]
    public async Task Plan_WhenCommandRemapsOwnedTarget_KeepsCommandRemapping()
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), "local change"));
        var planner = new PackUpdatePlanner(fileSystem);
        var managedFile = CreateManagedFile(
            "example",
            "docs/target.txt",
            "new content",
            declaredTarget: "target.txt"
        );

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "old content"),
            new PackInstallationPlan([managedFile])
            {
                Remappings =
                [
                    new ManagedFileRemapping(
                        "example",
                        "target.txt",
                        "docs/target.txt",
                        ManagedFileRemappingOrigin.Command
                    ),
                ],
            }
        );

        var plan = result.RequireValue();
        await Assert.That(plan.Actions).Count().IsEqualTo(2);
        await Assert
            .That(plan.Actions)
            .Contains(action => action is PreserveManagedFileUpdateAction);
        await Assert
            .That(plan.Remappings.Single().Origin)
            .IsEqualTo(ManagedFileRemappingOrigin.Command);
        await Assert.That(plan.Remappings.Single().EffectiveTarget).IsEqualTo("docs/target.txt");
    }

    [Test]
    public async Task Plan_WhenCommandRemapsUnchangedSource_AppliesMergeToNewTarget()
    {
        var fileSystem = CreateFileSystem(
            (ProjectPath("target.txt"), "source line\n"),
            (ProjectPath("docs/target.txt"), "local line\n")
        );
        var managedFile = CreateManagedFile(
            "example",
            "docs/target.txt",
            "source line\n",
            new PackManifest.PackManagedFileStrategy { Type = "merge", Method = "lines" },
            declaredTarget: "target.txt"
        );
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "target.txt", "source line\n"),
            new PackInstallationPlan([managedFile])
            {
                Remappings =
                [
                    new ManagedFileRemapping(
                        "example",
                        "target.txt",
                        "docs/target.txt",
                        ManagedFileRemappingOrigin.Command
                    ),
                ],
            }
        );

        var mergeAction = result
            .RequireValue()
            .Actions.Single(action => action is MergeLinesManagedFileUpdateAction);
        await Assert.That(ReadContents(mergeAction)).IsEqualTo("local line\nsource line\n");
    }

    [Test]
    public async Task Plan_WhenOwnerUsesLockedEffectiveTarget_PreservesDifferentObsoleteTarget()
    {
        var fileSystem = CreateFileSystem(
            (ProjectPath("locked.txt"), "retained source"),
            (ProjectPath("new-config.txt"), "local change")
        );
        var retainedPack = CreateLockFile("retained", "locked.txt", "retained source")
            .Packs.Single();
        var removedPack = CreateLockFile("removed", "new-config.txt", "old source").Packs.Single();
        var managedFile = CreateManagedFile(
            "retained",
            "new-config.txt",
            "retained source",
            declaredTarget: "locked.txt"
        );
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            new ProjectLockFile { SchemaVersion = 1, Packs = [retainedPack, removedPack] },
            new PackInstallationPlan([managedFile])
        );

        await Assert
            .That(result.RequireValue().Actions.Single())
            .IsTypeOf<PreserveManagedFileUpdateAction>();
    }

    [Test]
    public async Task Plan_WhenManagedTargetIgnored_DoesNotCreateDeleteAction()
    {
        var planner = new PackUpdatePlanner(
            CreateFileSystem((ProjectPath("target.txt"), "old content"))
        );

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

    [Test]
    public async Task Plan_WhenObsoleteTargetAncestorIsReparsePoint_ReturnsFailure()
    {
        var fileSystem = new MockFileSystem();
        var linkedDirectory = ProjectPath("linked");
        fileSystem.Directory.CreateDirectory(linkedDirectory);
        fileSystem.File.SetAttributes(
            linkedDirectory,
            FileAttributes.Directory | FileAttributes.ReparsePoint
        );
        var planner = new PackUpdatePlanner(fileSystem);

        var result = planner.Plan(
            _projectDirectory,
            CreateLockFile("example", "linked/target.txt", "old content"),
            new PackInstallationPlan([])
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("link or reparse point");
    }

    private static PlannedManagedFile CreateManagedFile(
        string packId,
        string target,
        string contents,
        PackManifest.PackManagedFileStrategy? strategy = null,
        string? declaredTarget = null,
        string? projectDirectory = null
    )
    {
        var absoluteProjectDirectory = projectDirectory ?? _projectDirectory;
        return new(
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
            ProjectPathForDirectory(absoluteProjectDirectory, target),
            target,
            strategy ?? PackManifest.PackManagedFileStrategy.CopyOverwrite
        );
    }

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

    private static string ProjectPathForDirectory(string projectDirectory, params string[] paths) =>
        Path.Combine([projectDirectory, .. paths]);

    private static string PacksPath(params string[] paths) =>
        Path.Combine([_packsDirectory, .. paths]);

    private static bool HostAliasesPathCase()
    {
        var directory = Directory.CreateTempSubdirectory("lunapack-case-alias");
        try
        {
            var probePath = Path.Combine(directory.FullName, "LunapackCaseAlias.txt");
            var aliasedPath = Path.Combine(directory.FullName, "lunapackcasealias.txt");
            File.WriteAllText(probePath, "probe");
            return File.Exists(aliasedPath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

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

    private static ProjectLockFile CreateSharedLockFile(
        string firstPackId,
        string secondPackId,
        string contents
    )
    {
        var first = CreateLockFile(firstPackId, "shared.txt", contents).Packs.Single();
        var second = CreateLockFile(secondPackId, "shared.txt", contents).Packs.Single();
        return new ProjectLockFile { SchemaVersion = 1, Packs = [first, second] };
    }

    private static string ComputeSha256(string contents) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents)));
}
