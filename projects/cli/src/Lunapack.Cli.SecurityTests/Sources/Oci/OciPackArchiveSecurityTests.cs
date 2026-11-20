using System.IO.Abstractions;
using System.IO.Compression;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.SecurityTests.Sources.Oci;

public sealed class OciPackArchiveSecurityTests
{
    [Test]
    public async Task Scenario_ExtractRejectsTraversalWithoutTouchingOutsidePath()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");
        var outsidePath = Path.Combine(root, "outside.txt");

        try
        {
            using var archive = CreateArchive(("../outside.txt", "untrusted"));

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(File.Exists(outsidePath)).IsFalse();
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractRejectsLinkEntryBeforeWriting()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");

        try
        {
            using var archive = CreateLinkArchive();

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("link-like");
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractRejectsFileDirectoryConflictBeforeWriting()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");

        try
        {
            using var archive = CreateArchive(
                ("conflict", "file"),
                ("conflict/child.txt", "child")
            );

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("file-directory conflict");
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractAcceptsExactEntryCountPerFileAndExpandedLimits()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");
        var limits = CreateLimits(
            maximumEntryCount: 2,
            maximumEntryBytes: 4,
            maximumExpandedBytes: 5
        );

        try
        {
            using var archive = CreateArchive(("first.txt", "1234"), ("second.txt", "x"));

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None,
                limits
            );

            await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
            await Assert
                .That(File.ReadAllText(Path.Combine(extractionRoot, "first.txt")))
                .IsEqualTo("1234");
            await Assert
                .That(File.ReadAllText(Path.Combine(extractionRoot, "second.txt")))
                .IsEqualTo("x");
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractRejectsEntryCountAboveLimitBeforeWriting()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");

        try
        {
            using var archive = CreateArchive(
                ("first.txt", "1"),
                ("second.txt", "2"),
                ("third.txt", "3")
            );

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None,
                CreateLimits(maximumEntryCount: 2)
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractRejectsSnapshotFileCountAboveLimitBeforeWriting()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");

        try
        {
            using var archive = CreateArchive(("first.txt", "1"), ("second.txt", "2"));

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None,
                CreateLimits(maximumFileCount: 1)
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert
                .That(result.ErrorKind)
                .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
            await Assert.That(result.Error).Contains("at least 2 files and directories");
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractRejectsImplicitDirectoryCountAboveLimitBeforeWriting()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");

        try
        {
            using var archive = CreateArchive(("nested/file.txt", "content"));

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None,
                CreateLimits(maximumFileCount: 1)
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert
                .That(result.ErrorKind)
                .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
            await Assert.That(result.Error).Contains("at least 2 files and directories");
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractRejectsFileAbovePerEntryLimitBeforeWriting()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");

        try
        {
            using var archive = CreateArchive(("large.txt", "1234"));

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None,
                CreateLimits(maximumEntryBytes: 3)
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractRejectsTotalExpandedSizeAboveLimitBeforeWriting()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");

        try
        {
            using var archive = CreateArchive(("first.txt", "123"), ("second.txt", "456"));

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None,
                CreateLimits(maximumEntryBytes: 3, maximumExpandedBytes: 5)
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractRejectsSnapshotBytesAboveLimitBeforeWriting()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");

        try
        {
            using var archive = CreateArchive(("first.txt", "123"), ("second.txt", "456"));

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None,
                CreateLimits(maximumSnapshotBytes: 5)
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert
                .That(result.ErrorKind)
                .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
            await Assert.That(result.Error).Contains("at least 6 bytes declared");
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_CreateAcceptsExactArchiveSizeLimitAndRejectsOneByteLess()
    {
        var root = CreateTemporaryRoot();
        var packDirectory = Path.Combine(root, "pack");
        Directory.CreateDirectory(packDirectory);
        File.WriteAllText(Path.Combine(packDirectory, "pack.yml"), "id: backend\nversion: 1.0.0\n");
        File.WriteAllText(Path.Combine(packDirectory, "README.md"), "Pack content.");
        var fileSystem = new FileSystem();
        var generousLimits = CreateLimits(
            maximumArchiveBytes: long.MaxValue,
            maximumEntryBytes: 1024,
            maximumExpandedBytes: 2048
        );

        try
        {
            using var baselineArchive = new MemoryStream();
            var baseline = await OciPackArchive.CreateAsync(
                fileSystem,
                packDirectory,
                baselineArchive,
                CancellationToken.None,
                generousLimits
            );
            await Assert.That(baseline.IsSuccess).IsTrue().Because(baseline.Error ?? string.Empty);

            var exactLimits = generousLimits with { MaximumArchiveBytes = baselineArchive.Length };
            using var exactArchive = new MemoryStream();
            var exact = await OciPackArchive.CreateAsync(
                fileSystem,
                packDirectory,
                exactArchive,
                CancellationToken.None,
                exactLimits
            );

            var overLimit = generousLimits with
            {
                MaximumArchiveBytes = baselineArchive.Length - 1,
            };
            using var rejectedArchive = new MemoryStream();
            var rejected = await OciPackArchive.CreateAsync(
                fileSystem,
                packDirectory,
                rejectedArchive,
                CancellationToken.None,
                overLimit
            );

            await Assert.That(exact.IsSuccess).IsTrue();
            await Assert.That(exactArchive.Length).IsEqualTo(baselineArchive.Length);
            await Assert.That(rejected.IsSuccess).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_CreateAcceptsExactPerFileAndExpandedLimits()
    {
        var root = CreateTemporaryRoot();
        var packDirectory = Directory.CreateDirectory(Path.Combine(root, "pack")).FullName;
        File.WriteAllText(Path.Combine(packDirectory, "first.txt"), "1234");
        File.WriteAllText(Path.Combine(packDirectory, "second.txt"), "x");
        var limits = CreateLimits(
            maximumEntryCount: 2,
            maximumArchiveBytes: 1024,
            maximumEntryBytes: 4,
            maximumExpandedBytes: 5
        );

        try
        {
            using var archive = new MemoryStream();
            var result = await OciPackArchive.CreateAsync(
                new FileSystem(),
                packDirectory,
                archive,
                CancellationToken.None,
                limits
            );

            await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_CreateRejectsSourceAboveEntryCountLimit()
    {
        var root = CreateTemporaryRoot();
        var packDirectory = Directory.CreateDirectory(Path.Combine(root, "pack")).FullName;
        File.WriteAllText(Path.Combine(packDirectory, "first.txt"), "1");
        File.WriteAllText(Path.Combine(packDirectory, "second.txt"), "2");
        File.WriteAllText(Path.Combine(packDirectory, "third.txt"), "3");

        try
        {
            using var archive = new MemoryStream();
            var result = await OciPackArchive.CreateAsync(
                new FileSystem(),
                packDirectory,
                archive,
                CancellationToken.None,
                CreateLimits(maximumEntryCount: 2)
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(archive.Length).IsEqualTo(0);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_CreateRejectsDirectoryCountAboveSnapshotItemLimit()
    {
        var root = CreateTemporaryRoot();
        var packDirectory = Directory.CreateDirectory(Path.Combine(root, "pack")).FullName;
        Directory.CreateDirectory(Path.Combine(packDirectory, "first"));
        File.WriteAllText(Path.Combine(packDirectory, "first", "content.txt"), "x");

        try
        {
            using var archive = new MemoryStream();
            var result = await OciPackArchive.CreateAsync(
                new FileSystem(),
                packDirectory,
                archive,
                CancellationToken.None,
                CreateLimits(maximumFileCount: 1)
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert
                .That(result.ErrorKind)
                .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
            await Assert.That(result.Error).Contains("2 entries observed");
            await Assert.That(archive.Length).IsEqualTo(0);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_CreateRejectsSourceFileAbovePerEntryLimit()
    {
        var root = CreateTemporaryRoot();
        var packDirectory = Directory.CreateDirectory(Path.Combine(root, "pack")).FullName;
        File.WriteAllText(Path.Combine(packDirectory, "large.txt"), "1234");

        try
        {
            using var archive = new MemoryStream();
            var result = await OciPackArchive.CreateAsync(
                new FileSystem(),
                packDirectory,
                archive,
                CancellationToken.None,
                CreateLimits(maximumEntryBytes: 3)
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(archive.Length).IsEqualTo(0);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_CreateRejectsSourceAboveTotalExpandedLimit()
    {
        var root = CreateTemporaryRoot();
        var packDirectory = Directory.CreateDirectory(Path.Combine(root, "pack")).FullName;
        File.WriteAllText(Path.Combine(packDirectory, "first.txt"), "123");
        File.WriteAllText(Path.Combine(packDirectory, "second.txt"), "456");

        try
        {
            using var archive = new MemoryStream();
            var result = await OciPackArchive.CreateAsync(
                new FileSystem(),
                packDirectory,
                archive,
                CancellationToken.None,
                CreateLimits(maximumEntryBytes: 3, maximumExpandedBytes: 5)
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(archive.Length).IsEqualTo(0);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractStillRejectsDuplicatePathsWithRaisedSizeLimits()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");

        try
        {
            using var archive = CreateArchive(
                ("duplicate.txt", "first"),
                ("duplicate.txt", "second")
            );

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None,
                CreateLimits(
                    maximumEntryCount: 100,
                    maximumArchiveBytes: long.MaxValue,
                    maximumEntryBytes: 1024,
                    maximumExpandedBytes: 4096,
                    maximumFileCount: 100,
                    maximumSnapshotBytes: 4096
                )
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("duplicate path");
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractStillRejectsUnsafePathWithRaisedSizeLimits()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");
        var outsidePath = Path.Combine(root, "outside.txt");

        try
        {
            using var archive = CreateArchive(("../outside.txt", "untrusted"));

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None,
                CreateLimits(
                    maximumEntryCount: 100,
                    maximumArchiveBytes: long.MaxValue,
                    maximumEntryBytes: 1024,
                    maximumExpandedBytes: 4096,
                    maximumFileCount: 100,
                    maximumSnapshotBytes: 4096
                )
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("unsafe path");
            await Assert.That(File.Exists(outsidePath)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_ExtractStillRejectsLinksWithRaisedSizeLimits()
    {
        var root = CreateTemporaryRoot();
        var extractionRoot = Path.Combine(root, "extracted");

        try
        {
            using var archive = CreateLinkArchive();

            var result = await OciPackArchive.ExtractAsync(
                new FileSystem(),
                archive,
                extractionRoot,
                CancellationToken.None,
                CreateLimits(
                    maximumEntryCount: 100,
                    maximumArchiveBytes: long.MaxValue,
                    maximumEntryBytes: 1024,
                    maximumExpandedBytes: 4096,
                    maximumFileCount: 100,
                    maximumSnapshotBytes: 4096
                )
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("link-like");
            await Assert.That(Directory.Exists(extractionRoot)).IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task Scenario_CreateRejectsSymbolicLinkInPack()
    {
        var root = CreateTemporaryRoot();
        var packDirectory = Directory.CreateDirectory(Path.Combine(root, "pack")).FullName;
        var outsidePath = Path.Combine(root, "outside.txt");
        var linkPath = Path.Combine(packDirectory, "linked.txt");
        File.WriteAllText(outsidePath, "outside content");

        try
        {
            try
            {
                File.CreateSymbolicLink(linkPath, outsidePath);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                Skip.Test($"Symbolic links are unavailable: {exception.Message}");
            }

            using var archive = new MemoryStream();
            var result = await OciPackArchive.CreateAsync(
                new FileSystem(),
                packDirectory,
                archive,
                CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("symbolic link or reparse point");
            await Assert.That(File.ReadAllText(outsidePath)).IsEqualTo("outside content");
        }
        finally
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            DeleteTemporaryRoot(root);
        }
    }

    private static OciPackArchive.Limits CreateLimits(
        int maximumEntryCount = 8,
        long maximumArchiveBytes = 1024,
        long maximumEntryBytes = 8,
        long maximumExpandedBytes = 16,
        int? maximumFileCount = null,
        long? maximumSnapshotBytes = null
    ) =>
        new(
            maximumEntryCount,
            maximumArchiveBytes,
            maximumEntryBytes,
            maximumExpandedBytes,
            maximumFileCount,
            maximumSnapshotBytes
        );

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

    private static MemoryStream CreateLinkArchive()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("link");
            entry.ExternalAttributes = unchecked((int)0xA0000000);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("target");
        }

        stream.Position = 0;
        return stream;
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lunapack-oci-security-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
