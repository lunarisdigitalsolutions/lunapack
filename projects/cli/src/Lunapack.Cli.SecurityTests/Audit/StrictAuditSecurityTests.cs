using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using Lunapack.Cli.Audit;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.SecurityTests.Audit;

public sealed class StrictAuditSecurityTests
{
    [Test]
    public async Task InspectManagedFiles_WhenTargetIsDriveRelative_ReturnsOperationError()
    {
        var fileSystem = new MockFileSystem();
        var service = new AuditService(fileSystem, new ProjectStateStore(fileSystem));
        var lockFile = CreateLockFile("Z:outside.txt", "secret");

        var result = service.InspectManagedFiles(
            OperatingSystem.IsWindows() ? @"C:\project" : "/project",
            lockFile,
            failFast: false
        );

        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Error).Contains("not a safe project-relative path");
    }

    [Test]
    public async Task InspectManagedFiles_WhenTargetCrossesDirectoryLink_ReturnsOperationError()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lunapack-security-{Guid.NewGuid():N}");
        var projectDirectory = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        var outsideDirectory = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var link = Path.Combine(projectDirectory, "linked");
        var outsidePath = Path.Combine(outsideDirectory, "managed.txt");
        File.WriteAllText(outsidePath, "outside content");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, outsideDirectory);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                Skip.Test($"Symbolic links are unavailable: {exception.Message}");
            }

            var fileSystem = new FileSystem();
            var service = new AuditService(fileSystem, new ProjectStateStore(fileSystem));
            var result = service.InspectManagedFiles(
                projectDirectory,
                CreateLockFile("linked/managed.txt", "outside content"),
                failFast: false
            );

            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Error).Contains("link or reparse point");
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ProjectLockFile CreateLockFile(string targetPath, string contents) =>
        new()
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "example",
                    PackPath = "example",
                    Version = "1.0.0",
                    ManagedFiles =
                    [
                        new ProjectLockFile.ManagedFile
                        {
                            TargetPath = targetPath,
                            Sha256 = Convert.ToHexString(
                                SHA256.HashData(Encoding.UTF8.GetBytes(contents))
                            ),
                        },
                    ],
                },
            ],
        };
}
