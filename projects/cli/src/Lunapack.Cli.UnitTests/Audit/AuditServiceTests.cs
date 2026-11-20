using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using Lunapack.Cli.Audit;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.UnitTests.Audit;

public sealed class AuditServiceTests
{
    private static readonly string _projectDirectory = OperatingSystem.IsWindows()
        ? @"C:\project"
        : "/project";

    [Test]
    public async Task InspectManagedFiles_WhenEveryPackAndLinkTargetMatches_ReturnsCurrent()
    {
        var fileSystem = new MockFileSystem();
        var service = CreateService(fileSystem);
        var lockFile = CreateLockFile(
            packs: [CreatePack(("pack.txt", "pack content"))],
            links: [CreateLink("agents", ("link.txt", "link content"))]
        );
        AddFile(fileSystem, "pack.txt", "pack content");
        AddFile(fileSystem, "link.txt", "link content");

        var reports = service
            .InspectManagedFiles(_projectDirectory, lockFile, failFast: false)
            .RequireValue();

        await Assert.That(reports).Count().IsEqualTo(2);
        await Assert
            .That(reports.All(report => report.Status is ManagedFileIntegrityStatus.Current))
            .IsTrue();
    }

    [Test]
    public async Task InspectManagedFiles_WhenPackTargetIsMissing_ReturnsMissingViolation()
    {
        var fileSystem = new MockFileSystem();
        var service = CreateService(fileSystem);
        var lockFile = CreateLockFile(packs: [CreatePack(("missing.txt", "expected"))]);

        var report = service
            .InspectManagedFiles(_projectDirectory, lockFile, failFast: false)
            .RequireValue()
            .Single();

        await Assert.That(report.TargetPath).IsEqualTo("missing.txt");
        await Assert.That(report.Status).IsEqualTo(ManagedFileIntegrityStatus.Missing);
        await Assert.That(report.IsViolation).IsTrue();
    }

    [Test]
    public async Task InspectManagedFiles_WhenLinkTargetIsModified_ReturnsModifiedViolation()
    {
        var fileSystem = new MockFileSystem();
        var service = CreateService(fileSystem);
        var lockFile = CreateLockFile(links: [CreateLink("agents", ("agents.txt", "expected"))]);
        AddFile(fileSystem, "agents.txt", "modified");

        var report = service
            .InspectManagedFiles(_projectDirectory, lockFile, failFast: false)
            .RequireValue()
            .Single();

        await Assert.That(report.Owner.Kind).IsEqualTo(ManagedRootKind.Link);
        await Assert.That(report.TargetPath).IsEqualTo("agents.txt");
        await Assert.That(report.Status).IsEqualTo(ManagedFileIntegrityStatus.Modified);
    }

    [Test]
    public async Task InspectManagedFiles_WhenAggregateScanFindsMultipleViolations_ReportsAllOwners()
    {
        var fileSystem = new MockFileSystem();
        var service = CreateService(fileSystem);
        var lockFile = CreateLockFile(
            packs: [CreatePack(("pack.txt", "expected"))],
            links: [CreateLink("agents", ("link.txt", "expected"))]
        );
        AddFile(fileSystem, "pack.txt", "modified");

        var reports = service
            .InspectManagedFiles(_projectDirectory, lockFile, failFast: false)
            .RequireValue();

        await Assert.That(reports.Count(report => report.IsViolation)).IsEqualTo(2);
        await Assert
            .That(
                reports.Select(report =>
                    $"{report.Owner.Kind}:{report.Owner.Name}:{report.TargetPath}"
                )
            )
            .IsEquivalentTo(["Pack:example:pack.txt", "Link:agents:link.txt"]);
    }

    [Test]
    public async Task InspectManagedFiles_WhenFailFastFindsViolation_StopsScanning()
    {
        var fileSystem = new MockFileSystem();
        var service = CreateService(fileSystem);
        var lockFile = CreateLockFile(
            packs: [CreatePack(("first.txt", "expected"), ("second.txt", "expected"))]
        );
        AddFile(fileSystem, "first.txt", "modified");
        AddFile(fileSystem, "second.txt", "modified");

        var reports = service
            .InspectManagedFiles(_projectDirectory, lockFile, failFast: true)
            .RequireValue();

        await Assert.That(reports).Count().IsEqualTo(1);
        await Assert.That(reports[0].TargetPath).IsEqualTo("first.txt");
    }

    [Test]
    public async Task InspectManagedFiles_WhenTargetCannotBeRead_ReturnsOperationError()
    {
        var fileSystem = new MockFileSystem();
        var service = CreateService(fileSystem);
        var lockFile = CreateLockFile(packs: [CreatePack(("locked.txt", "expected"))]);
        var targetPath = AddFile(fileSystem, "locked.txt", "expected");
        using var exclusiveAccess = fileSystem.File.Open(
            targetPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );

        var result = service.InspectManagedFiles(_projectDirectory, lockFile, failFast: false);

        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Error).Contains("Unable to read managed target 'locked.txt'");
    }

    private static AuditService CreateService(MockFileSystem fileSystem) =>
        new(fileSystem, new ProjectStateStore(fileSystem));

    private static ProjectLockFile CreateLockFile(
        IReadOnlyList<ProjectLockFile.ResolvedPack>? packs = null,
        IReadOnlyList<(string Name, ProjectLockFile.ResolvedLink Link)>? links = null
    )
    {
        var lockFile = new ProjectLockFile { SchemaVersion = 1, Packs = packs?.ToList() ?? [] };
        foreach (var (name, link) in links ?? [])
        {
            lockFile.Links.Add(name, link);
        }

        return lockFile;
    }

    private static ProjectLockFile.ResolvedPack CreatePack(
        params (string TargetPath, string Contents)[] files
    ) =>
        new()
        {
            Id = "example",
            PackPath = "example",
            Version = "1.0.0",
            ManagedFiles =
            [
                .. files.Select(file => new ProjectLockFile.ManagedFile
                {
                    TargetPath = file.TargetPath,
                    DeclaredTargetPath = file.TargetPath,
                    Sha256 = ComputeSha256(file.Contents),
                }),
            ],
        };

    private static (string Name, ProjectLockFile.ResolvedLink Link) CreateLink(
        string name,
        params (string TargetPath, string Contents)[] files
    ) =>
        (
            name,
            new ProjectLockFile.ResolvedLink
            {
                DefinitionSha256 = new string('0', 64),
                SourceIdentity = new ConfiguredSourceIdentity { Type = "local", Path = "source" },
                SourceName = "source",
                Files =
                [
                    .. files.Select(file => new ProjectLockFile.LinkFile
                    {
                        DeclaredTargetPath = file.TargetPath,
                        SourcePath = file.TargetPath,
                        TargetPath = file.TargetPath,
                        Sha256 = ComputeSha256(file.Contents),
                    }),
                ],
            }
        );

    private static string AddFile(MockFileSystem fileSystem, string path, string contents)
    {
        var targetPath = fileSystem.Path.GetFullPath(path, _projectDirectory);
        fileSystem.AddFile(targetPath, new MockFileData(contents));
        return targetPath;
    }

    private static string ComputeSha256(string contents) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents)));
}
