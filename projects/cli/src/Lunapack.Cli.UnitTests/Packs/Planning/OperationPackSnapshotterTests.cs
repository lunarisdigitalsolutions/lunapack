using System.IO.Abstractions;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests.Packs.Planning;

public sealed class OperationPackSnapshotterTests
{
    [Test]
    public async Task Snapshot_WhenSourceChanges_PreservesReadOnlyOperationContent()
    {
        using var workspace = new TestWorkspace();
        var packDirectory = CreatePack(workspace.Path);
        var snapshotRoot = Path.Combine(workspace.Path, "snapshot");
        var security = new OperationSnapshotSecurity();
        var snapshotter = new OperationPackSnapshotter(new FileSystem(), security);

        var result = snapshotter.Snapshot(
            CreateDiscoveredPack(workspace.Path, packDirectory),
            snapshotRoot
        );
        File.WriteAllText(Path.Combine(packDirectory, "content.txt"), "changed");
        var snapshotFile = Path.Combine(result.RequireValue().PackDirectory, "content.txt");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(File.ReadAllText(snapshotFile)).IsEqualTo("original");
        await Assert.That(() => File.WriteAllText(snapshotFile, "tampered")).ThrowsException();

        security.PrepareForDelete(new FileSystem(), snapshotRoot);
        GitTemporaryWorkspace.Delete(new FileSystem(), snapshotRoot);
        await Assert.That(Directory.Exists(snapshotRoot)).IsFalse();
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
}
