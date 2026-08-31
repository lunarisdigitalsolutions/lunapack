using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;
using Spectre.Console;

namespace Lunapack.Cli.SecurityTests.Packs.Planning;

public sealed class OperationPackSnapshotSecurityTests
{
    [Test]
    public async Task Snapshot_WhenPackContainsFileLink_SkipsLinkAndCopiesRegularFiles()
    {
        var root = CreateRoot();
        var security = new OperationSnapshotSecurity();
        var fileSystem = new FileSystem();
        var packDirectory = CreatePack(root);
        var snapshotRoot = Path.Combine(root, "snapshot");
        var link = Path.Combine(packDirectory, "linked.txt");
        File.WriteAllText(Path.Combine(root, "outside.txt"), "private");

        try
        {
            CreateFileLink(link, Path.Combine(root, "outside.txt"));
            var result = new OperationPackSnapshotter(
                fileSystem,
                security,
                CreateConsole()
            ).Snapshot(CreateDiscoveredPack(root, packDirectory), snapshotRoot);
            var snapshot =
                result.Value
                ?? throw new InvalidOperationException(result.Error ?? "Snapshot failed.");

            await Assert.That(result.IsSuccess).IsTrue();
            await Assert
                .That(File.Exists(Path.Combine(snapshot.PackDirectory, "linked.txt")))
                .IsFalse();
            await Assert
                .That(File.ReadAllText(Path.Combine(snapshot.PackDirectory, "content.txt")))
                .IsEqualTo("content");
        }
        finally
        {
            Cleanup(fileSystem, security, root, snapshotRoot);
        }
    }

    [Test]
    public async Task Snapshot_WhenPackContainsDirectoryLink_SkipsLinkAndCopiesRegularFiles()
    {
        var root = CreateRoot();
        var security = new OperationSnapshotSecurity();
        var fileSystem = new FileSystem();
        var packDirectory = CreatePack(root);
        var snapshotRoot = Path.Combine(root, "snapshot");
        var outsideDirectory = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        File.WriteAllText(Path.Combine(outsideDirectory, "private.txt"), "private");

        try
        {
            CreateDirectoryLink(Path.Combine(packDirectory, "linked"), outsideDirectory);
            var result = new OperationPackSnapshotter(
                fileSystem,
                security,
                CreateConsole()
            ).Snapshot(CreateDiscoveredPack(root, packDirectory), snapshotRoot);
            var snapshot =
                result.Value
                ?? throw new InvalidOperationException(result.Error ?? "Snapshot failed.");

            await Assert.That(result.IsSuccess).IsTrue();
            await Assert
                .That(Directory.Exists(Path.Combine(snapshot.PackDirectory, "linked")))
                .IsFalse();
            await Assert
                .That(File.ReadAllText(Path.Combine(snapshot.PackDirectory, "content.txt")))
                .IsEqualTo("content");
        }
        finally
        {
            Cleanup(fileSystem, security, root, snapshotRoot);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lunapack-security-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreatePack(string root)
    {
        var packDirectory = Directory
            .CreateDirectory(Path.Combine(root, "source", "example"))
            .FullName;
        File.WriteAllText(Path.Combine(packDirectory, "pack.yml"), "id: example\nversion: 1.0.0\n");
        File.WriteAllText(Path.Combine(packDirectory, "content.txt"), "content");
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

    private static CliConsole CreateConsole() =>
        new(
            AnsiConsole.Create(
                new AnsiConsoleSettings
                {
                    Ansi = AnsiSupport.No,
                    ColorSystem = ColorSystemSupport.NoColors,
                    Out = new AnsiConsoleOutput(TextWriter.Null),
                }
            ),
            CliLogLevel.Info
        );

    private static void CreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Skip.Test($"File symbolic links are unavailable: {exception.Message}");
        }
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Skip.Test($"Directory symbolic links are unavailable: {exception.Message}");
        }
    }

    private static void Cleanup(
        IFileSystem fileSystem,
        OperationSnapshotSecurity security,
        string root,
        string snapshotRoot
    )
    {
        if (Directory.Exists(snapshotRoot))
        {
            security.PrepareForDelete(fileSystem, snapshotRoot);
        }

        GitTemporaryWorkspace.Delete(fileSystem, root);
    }
}
