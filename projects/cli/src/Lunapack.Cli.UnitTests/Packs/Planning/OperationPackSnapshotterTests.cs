using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Packs.Planning;

public sealed class OperationPackSnapshotterTests
{
    [Test]
    public async Task Snapshot_WhenPackContainsReparsePoint_SkipsEntryAndCopiesOtherFiles()
    {
        var fileSystem = new MockFileSystem();
        var root = fileSystem.Path.GetFullPath("workspace");
        var packDirectory = fileSystem.Path.Combine(root, "source", "example");
        var linkedFile = fileSystem.Path.Combine(packDirectory, "linked.txt");
        var snapshotRoot = fileSystem.Path.Combine(root, "snapshot");
        var ansiConsole = new SpectreTestConsole();
        fileSystem.AddDirectory(packDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(packDirectory, "pack.yml"),
            new MockFileData("id: example\nversion: 1.0.0\n")
        );
        fileSystem.AddFile(linkedFile, new MockFileData("private"));
        fileSystem.AddFile(
            fileSystem.Path.Combine(packDirectory, "retained.txt"),
            new MockFileData("retained")
        );
        fileSystem.File.SetAttributes(linkedFile, FileAttributes.ReparsePoint);

        var result = new OperationPackSnapshotter(
            fileSystem,
            new NoOpOperationSnapshotSecurity(),
            new CliConsole(ansiConsole, CliLogLevel.Info)
        ).Snapshot(CreateDiscoveredPack(root, packDirectory), snapshotRoot);
        var snapshotPackDirectory = result.RequireValue().PackDirectory;

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(
                fileSystem.File.Exists(fileSystem.Path.Combine(snapshotPackDirectory, "linked.txt"))
            )
            .IsFalse();
        await Assert
            .That(
                fileSystem.File.ReadAllText(
                    fileSystem.Path.Combine(snapshotPackDirectory, "retained.txt")
                )
            )
            .IsEqualTo("retained");
        await Assert.That(ansiConsole.Output).Contains("linked.txt");
        await Assert.That(ansiConsole.Output).Contains("Skipping unsupported pack snapshot entry");
    }

    [Test]
    public async Task Snapshot_WhenSourceChanges_PreservesReadOnlyOperationContent()
    {
        using var workspace = new TestWorkspace();
        var packDirectory = CreatePack(workspace.Path);
        var snapshotRoot = Path.Combine(workspace.Path, "snapshot");
        var security = new OperationSnapshotSecurity();
        var snapshotter = new OperationPackSnapshotter(
            new FileSystem(),
            security,
            TestConsole.Create()
        );

        var result = snapshotter.Snapshot(
            CreateDiscoveredPack(workspace.Path, packDirectory),
            snapshotRoot
        );
        File.WriteAllText(Path.Combine(packDirectory, "content.txt"), "changed");
        var snapshotFile = Path.Combine(result.RequireValue().PackDirectory, "content.txt");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(File.ReadAllText(snapshotFile)).IsEqualTo("original");
        await Assert.That(IsReadOnly(snapshotFile)).IsTrue();

        security.PrepareForDelete(new FileSystem(), snapshotRoot);
        GitTemporaryWorkspace.Delete(new FileSystem(), snapshotRoot);
        await Assert.That(Directory.Exists(snapshotRoot)).IsFalse();
    }

    [Test]
    public async Task Snapshot_WhenFilesAndBytesMatchLimits_AcceptsBoundary()
    {
        var fileSystem = new MockFileSystem();
        var root = fileSystem.Path.GetFullPath("workspace");
        var packDirectory = fileSystem.Path.Combine(root, "source", "example");
        var manifestPath = fileSystem.Path.Combine(packDirectory, "pack.yml");
        var contentPath = fileSystem.Path.Combine(packDirectory, "content.txt");
        var snapshotRoot = fileSystem.Path.Combine(root, "snapshot");
        fileSystem.AddFile(manifestPath, new MockFileData("manifest"));
        fileSystem.AddFile(contentPath, new MockFileData("content"));
        var resourceLimits = ResourceLimits.Default with
        {
            FilesPerPack = 2,
            PackSnapshotBytes = 15,
        };

        var result = new OperationPackSnapshotter(
            fileSystem,
            new NoOpOperationSnapshotSecurity(),
            TestConsole.Create()
        ).Snapshot(CreateDiscoveredPack(root, packDirectory), snapshotRoot, resourceLimits);

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
    }

    [Test]
    public async Task Snapshot_WhenFileCountExceedsLimit_RemovesPartialSnapshot()
    {
        var fileSystem = new MockFileSystem();
        var root = fileSystem.Path.GetFullPath("workspace");
        var packDirectory = fileSystem.Path.Combine(root, "source", "example");
        var snapshotRoot = fileSystem.Path.Combine(root, "snapshot");
        fileSystem.AddFile(
            fileSystem.Path.Combine(packDirectory, "pack.yml"),
            new MockFileData("manifest")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(packDirectory, "content.txt"),
            new MockFileData("content")
        );
        var resourceLimits = ResourceLimits.Default with { FilesPerPack = 1 };

        var result = new OperationPackSnapshotter(
            fileSystem,
            new NoOpOperationSnapshotSecurity(),
            TestConsole.Create()
        ).Snapshot(CreateDiscoveredPack(root, packDirectory), snapshotRoot, resourceLimits);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("2 entries observed");
        await Assert.That(fileSystem.Directory.Exists(snapshotRoot)).IsFalse();
    }

    [Test]
    public async Task Snapshot_WhenCopiedBytesExceedLimit_RemovesPartialSnapshot()
    {
        var fileSystem = new MockFileSystem();
        var root = fileSystem.Path.GetFullPath("workspace");
        var packDirectory = fileSystem.Path.Combine(root, "source", "example");
        var snapshotRoot = fileSystem.Path.Combine(root, "snapshot");
        fileSystem.AddFile(
            fileSystem.Path.Combine(packDirectory, "pack.yml"),
            new MockFileData("manifest")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(packDirectory, "content.txt"),
            new MockFileData("content")
        );
        var resourceLimits = ResourceLimits.Default with { PackSnapshotBytes = 14 };

        var result = new OperationPackSnapshotter(
            fileSystem,
            new NoOpOperationSnapshotSecurity(),
            TestConsole.Create()
        ).Snapshot(CreateDiscoveredPack(root, packDirectory), snapshotRoot, resourceLimits);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("byte limit");
        await Assert.That(result.Error).Contains("at least 15 bytes observed");
        await Assert.That(fileSystem.Directory.Exists(snapshotRoot)).IsFalse();
    }

    [Test]
    public async Task Snapshot_WhenDirectoryTreeExceedsLimit_RemovesPartialSnapshot()
    {
        var fileSystem = new MockFileSystem();
        var root = fileSystem.Path.GetFullPath("workspace");
        var packDirectory = fileSystem.Path.Combine(root, "source", "example");
        var snapshotRoot = fileSystem.Path.Combine(root, "snapshot");
        fileSystem.AddFile(
            fileSystem.Path.Combine(packDirectory, "pack.yml"),
            new MockFileData("manifest")
        );
        fileSystem.AddDirectory(fileSystem.Path.Combine(packDirectory, "first"));
        fileSystem.AddDirectory(fileSystem.Path.Combine(packDirectory, "second"));

        var result = new OperationPackSnapshotter(
            fileSystem,
            new NoOpOperationSnapshotSecurity(),
            TestConsole.Create()
        ).Snapshot(
            CreateDiscoveredPack(root, packDirectory),
            snapshotRoot,
            ResourceLimits.Default with
            {
                FilesPerPack = 1,
            }
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("1 file and directory limit");
        await Assert.That(result.Error).Contains("at least 2 entries observed");
        await Assert.That(fileSystem.Directory.Exists(snapshotRoot)).IsFalse();
    }

    [Test]
    public async Task Snapshot_WhenFileAndDirectoryShareLimit_RejectsCombinedCount()
    {
        var fileSystem = new MockFileSystem();
        var root = fileSystem.Path.GetFullPath("workspace");
        var packDirectory = fileSystem.Path.Combine(root, "source", "example");
        var snapshotRoot = fileSystem.Path.Combine(root, "snapshot");
        fileSystem.AddFile(
            fileSystem.Path.Combine(packDirectory, "pack.yml"),
            new MockFileData("manifest")
        );
        fileSystem.AddDirectory(fileSystem.Path.Combine(packDirectory, "directory"));

        var result = new OperationPackSnapshotter(
            fileSystem,
            new NoOpOperationSnapshotSecurity(),
            TestConsole.Create()
        ).Snapshot(
            CreateDiscoveredPack(root, packDirectory),
            snapshotRoot,
            ResourceLimits.Default with
            {
                FilesPerPack = 1,
            }
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("1 file and directory limit");
        await Assert.That(result.Error).Contains("at least 2 entries observed");
        await Assert.That(fileSystem.Directory.Exists(snapshotRoot)).IsFalse();
    }

    [Test]
    public async Task Enumerate_WhenSourceListExceedsLimit_StopsBeforeRetainingExtraFile()
    {
        var fileSystem = new MockFileSystem();
        var packDirectory = fileSystem.Path.GetFullPath("pack");
        fileSystem.AddFile(
            fileSystem.Path.Combine(packDirectory, "first.txt"),
            new MockFileData("first")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(packDirectory, "second.txt"),
            new MockFileData("second")
        );

        var result = PackSnapshotFileEnumerator.Enumerate(
            fileSystem,
            packDirectory,
            "example",
            ResourceLimits.Default with
            {
                FilesPerPack = 1,
            }
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert
            .That(result.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(result.Value).IsNull();
    }

    private static bool IsReadOnly(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return (File.GetAttributes(path) & FileAttributes.ReadOnly) != FileAttributes.Normal;
        }

        var writePermissions =
            UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        return (File.GetUnixFileMode(path) & writePermissions) == UnixFileMode.None;
    }

    private static string CreatePack(string root)
    {
        var packDirectory = Directory
            .CreateDirectory(Path.Combine(root, "source", "example"))
            .FullName;
        File.WriteAllText(Path.Combine(packDirectory, "pack.yml"), "id: example\nversion: 1.0.0\n");
        File.WriteAllText(Path.Combine(packDirectory, "content.txt"), "original");
        return packDirectory;
    }

    private static DiscoveredPack CreateDiscoveredPack(string root, string packDirectory) =>
        new(
            Path.Combine(root, "source"),
            packDirectory,
            new PackManifest { Id = "example", Version = "1.0.0" },
            "local",
            ConfiguredSourceIdentity.CreateLocal("source")
        );

    private sealed class NoOpOperationSnapshotSecurity : IOperationSnapshotSecurity
    {
        public void ApplyDirectory(string path) { }

        public void ApplyFile(string path) { }

        public void MakeReadOnly(IFileSystem fileSystem, string root) { }

        public void PrepareForDelete(IFileSystem fileSystem, string root) { }
    }
}
