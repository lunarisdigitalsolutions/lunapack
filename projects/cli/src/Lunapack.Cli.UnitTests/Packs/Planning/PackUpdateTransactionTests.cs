using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests.Packs.Planning;

public sealed class PackUpdateTransactionTests
{
    private static readonly string _projectDirectory = Path.GetFullPath("project");
    private static readonly string _packsDirectory = Path.GetFullPath("packs");

    [Test]
    public async Task Apply_WhenTargetAncestorIsReparsePoint_RejectsPlanBeforeMutation()
    {
        var fileSystem = CreateFileSystem();
        var aliasDirectory = ProjectPath("linked");
        fileSystem.Directory.CreateDirectory(aliasDirectory);
        fileSystem.File.SetAttributes(
            aliasDirectory,
            FileAttributes.Directory | FileAttributes.ReparsePoint
        );
        var targetPath = ProjectPath("linked", "managed.txt");
        var transaction = new PackUpdateTransaction(fileSystem, TestConsole.Create());

        var result = transaction.Apply(
            _projectDirectory,
            new PackUpdatePlan([
                new WriteManagedRootFileUpdateAction(
                    new ManagedRootOwner(ManagedRootKind.Link, "example"),
                    new ManagedRootFile(
                        "source.txt",
                        "linked/managed.txt",
                        "linked/managed.txt",
                        new string('0', 64)
                    ),
                    targetPath,
                    "replacement"u8.ToArray()
                ),
            ])
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("link or reparse point");
        await Assert.That(fileSystem.File.Exists(targetPath)).IsFalse();
    }

    [Test]
    public async Task Apply_WhenBackupCopyRolledBack_RestoresTargetAndRemovesBackup()
    {
        var fileSystem = CreateFileSystem((ProjectPath("target.txt"), "old content"));
        var target = CreateManagedFile("target.txt", "new content");
        var transaction = new PackUpdateTransaction(fileSystem, TestConsole.Create());

        var result = transaction.Apply(
            _projectDirectory,
            new PackUpdatePlan([
                new BackupAndCopyManagedFileUpdateAction(target, null, ProjectPath("target.txt.1")),
            ])
        );

        await Assert.That(ReadFile(fileSystem, ProjectPath("target.txt"))).IsEqualTo("new content");
        await Assert.That(fileSystem.File.Exists(ProjectPath("target.txt.1"))).IsTrue();

        result.RequireValue().Restore();

        await Assert.That(ReadFile(fileSystem, ProjectPath("target.txt"))).IsEqualTo("old content");
        await Assert.That(fileSystem.File.Exists(ProjectPath("target.txt.1"))).IsFalse();
    }

    [Test]
    public async Task Apply_WhenMixedActionsRolledBack_RestoresDeletedAndRemovesCreatedTargets()
    {
        var fileSystem = CreateFileSystem((ProjectPath("removed.txt"), "old content"));
        var createdFile = CreateManagedFile("created.txt", "new content");
        var previousPack = new ProjectLockFile.ResolvedPack
        {
            Id = "example",
            Version = "1.0.0",
            SourcePath = "packs",
            PackPath = "example",
            ManagedFiles =
            [
                new ProjectLockFile.ManagedFile { TargetPath = "removed.txt", Sha256 = "unused" },
            ],
        };
        var transaction = new PackUpdateTransaction(fileSystem, TestConsole.Create());

        var result = transaction.Apply(
            _projectDirectory,
            new PackUpdatePlan([
                new CreateManagedFileUpdateAction(createdFile),
                new DeleteManagedFileUpdateAction(
                    new ManagedRootOwner(
                        ManagedRootKind.Pack,
                        previousPack.Id,
                        previousPack.Version
                    ),
                    new ManagedRootFile(
                        previousPack.PackPath,
                        previousPack.ManagedFiles.Single().TargetPath,
                        previousPack.ManagedFiles.Single().TargetPath,
                        previousPack.ManagedFiles.Single().Sha256
                    ),
                    ProjectPath("removed.txt")
                ),
            ])
        );

        await Assert.That(fileSystem.File.Exists(ProjectPath("created.txt"))).IsTrue();
        await Assert.That(fileSystem.File.Exists(ProjectPath("removed.txt"))).IsFalse();

        result.RequireValue().Restore();

        await Assert.That(fileSystem.File.Exists(ProjectPath("created.txt"))).IsFalse();
        await Assert
            .That(ReadFile(fileSystem, ProjectPath("removed.txt")))
            .IsEqualTo("old content");
    }

    private static PlannedManagedFile CreateManagedFile(string target, string contents) =>
        new(
            new DiscoveredPack(
                _packsDirectory,
                PacksPath("example"),
                new PackManifest
                {
                    Id = "example",
                    Version = "2.0.0",
                    ManagedFiles = [],
                }
            ),
            PacksPath("example", "source.txt"),
            target,
            Encoding.UTF8.GetBytes(contents),
            ProjectPath(target),
            target,
            PackManifest.PackManagedFileStrategy.CopyOverwrite
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

    private static string ReadFile(MockFileSystem fileSystem, string path) =>
        fileSystem.File.ReadAllText(path);
}
