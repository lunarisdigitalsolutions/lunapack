using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Doctor;

namespace Lunapack.Cli.UnitTests.Doctor;

public sealed class DoctorTemporaryFileProbeTests
{
    [Test]
    public async Task Probe_WhenTempDescendantIsReparsePoint_Fails()
    {
        var fileSystem = new MockFileSystem();
        var tempDirectory = fileSystem.Path.GetFullPath(Path.GetTempPath());
        fileSystem.AddDirectory(tempDirectory);
        var directory = fileSystem.Path.Combine(tempDirectory, "lunapack-doctor-probe-link");
        fileSystem.AddDirectory(directory);
        fileSystem.File.SetAttributes(
            directory,
            FileAttributes.Directory | FileAttributes.ReparsePoint
        );

        var result = new DoctorTemporaryFileProbe(fileSystem).Probe(directory);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("link or reparse point");
    }

    [Test]
    public async Task Probe_WhenDirectoryExists_LeavesNoTemporaryFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "lunapack-doctor-probe",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var probe = new DoctorTemporaryFileProbe(new FileSystem());

            var result = probe.Probe(directory);

            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(Directory.EnumerateFiles(directory)).IsEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Probe_WhenTargetIsAFile_DoesNotModifyIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lunapack-doctor-{Guid.NewGuid():N}");
        File.WriteAllText(path, "sentinel");
        try
        {
            var probe = new DoctorTemporaryFileProbe(new FileSystem());

            var result = probe.Probe(path);

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(File.ReadAllText(path)).IsEqualTo("sentinel");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Probe_WhenDirectoryIsLinked_RejectsTarget()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lunapack-doctor-probe-link",
            Guid.NewGuid().ToString("N")
        );
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or PlatformNotSupportedException
                )
            {
                return;
            }

            var result = new DoctorTemporaryFileProbe(new FileSystem()).Probe(link);

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(Directory.EnumerateFiles(target)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
