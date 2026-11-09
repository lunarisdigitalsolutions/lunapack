using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.UnitTests.Sources.Oci;

public sealed class OciPackArchiveTests
{
    [Test]
    public async Task Scenario_CreateAndExtract_PreservesRootFilesNestedPathsAndDotfiles()
    {
        var fileSystem = new MockFileSystem();
        var sourceRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "source");
        fileSystem.AddFile(
            fileSystem.Path.Combine(sourceRoot, "pack.yml"),
            new MockFileData("id: backend\nversion: 1.0.0\n")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(sourceRoot, "README.md"),
            new MockFileData("readme")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(sourceRoot, "files", ".editorconfig"),
            new MockFileData("root = true")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(sourceRoot, "scripts", "setup.ps1"),
            new MockFileData("Write-Output ready")
        );

        using var archive = new MemoryStream();
        var created = await OciPackArchive.CreateAsync(
            fileSystem,
            sourceRoot,
            archive,
            CancellationToken.None
        );

        archive.Position = 0;
        var extractionRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "extracted");
        var extracted = await OciPackArchive.ExtractAsync(
            fileSystem,
            archive,
            extractionRoot,
            CancellationToken.None
        );

        await Assert.That(created.IsSuccess).IsTrue();
        await Assert.That(extracted.IsSuccess).IsTrue();
        await Assert.That(extracted.Value).Contains("pack.yml");
        await Assert
            .That(fileSystem.File.ReadAllText(fileSystem.Path.Combine(extractionRoot, "pack.yml")))
            .IsEqualTo("id: backend\nversion: 1.0.0\n");
        await Assert
            .That(
                fileSystem.File.ReadAllText(
                    fileSystem.Path.Combine(extractionRoot, "files", ".editorconfig")
                )
            )
            .IsEqualTo("root = true");
        await Assert
            .That(
                fileSystem.File.ReadAllText(
                    fileSystem.Path.Combine(extractionRoot, "scripts", "setup.ps1")
                )
            )
            .IsEqualTo("Write-Output ready");
    }

    [Test]
    [Arguments("../escape.txt")]
    [Arguments("/rooted.txt")]
    [Arguments("C:\\Windows\\file.txt")]
    [Arguments("safe\\..\\escape.txt")]
    public async Task Scenario_Extract_RejectsUnsafePathsWithoutWritingOutsideRoot(string path)
    {
        var fileSystem = new MockFileSystem();
        using var archive = CreateArchive((path, "unsafe"));
        var extractionRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "rejected");

        var result = await OciPackArchive.ExtractAsync(
            fileSystem,
            archive,
            extractionRoot,
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(fileSystem.Directory.Exists(extractionRoot)).IsFalse();
    }

    [Test]
    public async Task Scenario_Extract_RejectsCaseFoldedDuplicateDestinations()
    {
        var fileSystem = new MockFileSystem();
        using var archive = CreateArchive(("Files/one.txt", "a"), ("files/one.txt", "b"));
        var extractionRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "duplicate");

        var result = await OciPackArchive.ExtractAsync(
            fileSystem,
            archive,
            extractionRoot,
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(fileSystem.Directory.Exists(extractionRoot)).IsFalse();
    }

    [Test]
    public async Task Scenario_Extract_RemovesTemporaryFilesWhenArchiveIsCorrupt()
    {
        var fileSystem = new MockFileSystem();
        using var archive = new MemoryStream([1, 2, 3, 4]);
        var extractionRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "corrupt");

        var result = await OciPackArchive.ExtractAsync(
            fileSystem,
            archive,
            extractionRoot,
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(fileSystem.Directory.Exists(extractionRoot)).IsFalse();
    }

    private static MemoryStream CreateArchive(params (string Path, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
