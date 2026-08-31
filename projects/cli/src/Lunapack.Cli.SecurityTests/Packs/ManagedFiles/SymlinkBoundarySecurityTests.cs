using System.Diagnostics;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Trust;
using Spectre.Console;

namespace Lunapack.Cli.SecurityTests.Packs.ManagedFiles;

public sealed class SymlinkBoundarySecurityTests
{
    [Test]
    public async Task Apply_WhenTargetIsHardLink_ReplacesOnlyProjectEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lunapack-security-{Guid.NewGuid():N}");
        var projectDirectory = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        var outsidePath = Path.Combine(root, "outside.txt");
        var targetPath = Path.Combine(projectDirectory, "managed.txt");
        File.WriteAllText(outsidePath, "outside content");

        try
        {
            CreateHardLink(targetPath, outsidePath);

            var action = new WriteManagedRootFileUpdateAction(
                new ManagedRootOwner(ManagedRootKind.Link, "example"),
                new ManagedRootFile(
                    "source.txt",
                    "managed.txt",
                    "managed.txt",
                    new string('0', 64)
                ),
                targetPath,
                "replacement"u8.ToArray()
            );
            var transaction = new PackUpdateTransaction(new FileSystem(), CreateConsole());

            var result = transaction.Apply(projectDirectory, new PackUpdatePlan([action]));

            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("replacement");
            await Assert.That(File.ReadAllText(outsidePath)).IsEqualTo("outside content");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task Apply_WhenTargetAncestorIsSymbolicLink_PreservesOutsideFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lunapack-security-{Guid.NewGuid():N}");
        var projectDirectory = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        var outsideDirectory = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var outsidePath = Path.Combine(outsideDirectory, "managed.txt");
        var link = Path.Combine(projectDirectory, "linked");
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

            var action = new WriteManagedRootFileUpdateAction(
                new ManagedRootOwner(ManagedRootKind.Link, "example"),
                new ManagedRootFile(
                    "source.txt",
                    "linked/managed.txt",
                    "linked/managed.txt",
                    new string('0', 64)
                ),
                Path.Combine(link, "managed.txt"),
                "replacement"u8.ToArray()
            );
            var transaction = new PackUpdateTransaction(new FileSystem(), CreateConsole());

            var result = transaction.Apply(projectDirectory, new PackUpdatePlan([action]));

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("link or reparse point");
            await Assert.That(File.ReadAllText(outsidePath)).IsEqualTo("outside content");
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

    [Test]
    public async Task ValidateExisting_WhenDirectoryIsSymbolicLink_RejectsBoundaryAlias()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lunapack-security-{Guid.NewGuid():N}");
        var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
        var link = Path.Combine(root, "settings-link");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                Skip.Test($"Symbolic links are unavailable: {exception.Message}");
            }

            var error = UserSettingsPathSecurity.ValidateExisting(
                new FileSystem(),
                link,
                directory: true
            );

            await Assert.That(error).Contains("cannot be a link or reparse point");
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

    private static void CreateHardLink(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "fsutil.exe" : "ln",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("hardlink");
            startInfo.ArgumentList.Add("create");
            startInfo.ArgumentList.Add(linkPath);
            startInfo.ArgumentList.Add(targetPath);
        }
        else
        {
            startInfo.ArgumentList.Add(targetPath);
            startInfo.ArgumentList.Add(linkPath);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Skip.Test("Hard-link tool could not be started.");
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                Skip.Test(
                    $"Hard links are unavailable: {process.StandardError.ReadToEnd().Trim()}"
                );
            }
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or PlatformNotSupportedException
            )
        {
            Skip.Test($"Hard links are unavailable: {exception.Message}");
        }
    }
}
