using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Sbom;

public sealed class SbomCommandTests
{
    [Test]
    public async Task Sbom_WhenHelpRequested_ListsTargetsAndNonAttestationLimit()
    {
        using var workspace = new TestWorkspace();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--help"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output.ToString()).Contains("cyclonedx-1.6");
        await Assert.That(output.ToString()).Contains("spdx-3.0.1");
        await Assert.That(output.ToString()).Contains("not a signature or attestation");
        await Assert.That(error.ToString()).IsEmpty();
    }

    [Test]
    public async Task Sbom_WhenTargetIsUnsupported_WritesOnlyErrorToStandardError()
    {
        using var workspace = new TestWorkspace();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--target", "cyclonedx-unknown"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("Unsupported SBOM target");
    }

    [Test]
    public async Task Sbom_WhenLogLevelIsInvalid_WritesOnlyErrorToStandardError()
    {
        using var workspace = new TestWorkspace();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--target", "cyclonedx-1.6", "--log-level", "unsupported"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("Log level must be");
    }

    [Test]
    public async Task Sbom_WhenWritingToStdout_EmitsOnlyRawJsonEvenWithDebugLogging()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var previousConsoleOutputLength = ansiConsole.Output.Length;
        using var output = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--target", "cyclonedx-1.6", "--log-level", "debug"],
            workspace.Path,
            output
        );

        using var document = JsonDocument.Parse(output.ToString());
        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(document.RootElement.GetProperty("bomFormat").GetString())
            .IsEqualTo("CycloneDX");
        await Assert.That(ansiConsole.Output[previousConsoleOutputLength..]).IsEmpty();
    }

    [Test]
    public async Task Sbom_WhenOutputPathUsesWindowsSeparators_WritesJsonInsideProject()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "artifacts"));
        using var output = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--target", "cyclonedx-1.6", "--output", @"artifacts\inventory.json"],
            workspace.Path,
            output
        );
        var documentPath = Path.Combine(workspace.Path, "artifacts", "inventory.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(documentPath));

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output.ToString()).IsEmpty();
        await Assert
            .That(document.RootElement.GetProperty("bomFormat").GetString())
            .IsEqualTo("CycloneDX");
    }

    [Test]
    public async Task Sbom_WhenOutputDestinationCannotBeWritten_ReportsFailureWithoutStdout()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var destination = Path.Combine(workspace.Path, "inventory.json");
        Directory.CreateDirectory(destination);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--target", "cyclonedx-1.6", "--output", "inventory.json"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("Unable to write SBOM");
        await Assert.That(Directory.Exists(destination)).IsTrue();
        await Assert.That(Directory.EnumerateFiles(workspace.Path, "*.tmp")).IsEmpty();
    }

    [Test]
    public async Task Sbom_WhenOutputPathCrossesMockReparsePoint_RejectsAlias()
    {
        var fileSystem = new MockFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var userSettings = new UserSettingsStore(fileSystem, @"C:\profile");
        var application = new CliApplication(
            fileSystem,
            TestConsole.CreateAnsiConsole(),
            userSettingsStore: userSettings
        );
        await application.RunAsync(["init"], projectDirectory);
        var aliasPath = fileSystem.Path.Combine(projectDirectory, "linked");
        fileSystem.AddDirectory(aliasPath);
        fileSystem.File.SetAttributes(
            aliasPath,
            FileAttributes.Directory | FileAttributes.ReparsePoint
        );
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await application.RunAsync(
            ["sbom", "--target", "spdx-2.3", "--output", "linked/inventory.json"],
            projectDirectory,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("link or reparse point");
        await Assert
            .That(fileSystem.File.Exists(fileSystem.Path.Combine(aliasPath, "inventory.json")))
            .IsFalse();
    }

    [Test]
    public async Task Sbom_WhenWritingToStdout_PreservesProjectState()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var lockPath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var originalConfiguration = await File.ReadAllTextAsync(configurationPath);
        var originalLock = await File.ReadAllTextAsync(lockPath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--target", "spdx-3.0.1"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(error.ToString()).IsEmpty();
        await Assert
            .That(await File.ReadAllTextAsync(configurationPath))
            .IsEqualTo(originalConfiguration);
        await Assert.That(await File.ReadAllTextAsync(lockPath)).IsEqualTo(originalLock);
    }

    [Test]
    public async Task Sbom_WhenOutputCollidesWithProjectState_PreservesProjectConfiguration()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var originalConfiguration = await File.ReadAllTextAsync(configurationPath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--target", "cyclonedx-1.6", "--output", @".\lunapack.yml"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("conflicts with protected project path");
        await Assert
            .That(await File.ReadAllTextAsync(configurationPath))
            .IsEqualTo(originalConfiguration);
    }

    [Test]
    public async Task Sbom_WhenOutputCollidesWithManagedFile_PreservesManagedFile()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var state = new ProjectState
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Sources =
                [
                    new ProjectConfiguration.LocalSource { Name = "local", Path = "source" },
                ],
                Packs = [new ProjectConfiguration.RequestedPack { Id = "owned" }],
            },
            LockFile = new ProjectLockFile
            {
                SchemaVersion = 1,
                Packs =
                [
                    new ProjectLockFile.ResolvedPack
                    {
                        Id = "owned",
                        Version = "1.0.0",
                        PackPath = "owned",
                        SourceName = "local",
                        SourceIdentity = ConfiguredSourceIdentity.CreateLocal("source"),
                        SourcePath = "source",
                        ManagedFiles =
                        [
                            new ProjectLockFile.ManagedFile
                            {
                                DeclaredTargetPath = "docs/owned.txt",
                                TargetPath = "docs/owned.txt",
                                Sha256 = new string('a', 64),
                            },
                        ],
                    },
                ],
            },
        };
        var save = await workspace.StateStore.SaveAllowingUnavailableSourcesAsync(
            workspace.Path,
            state
        );
        await Assert.That(save.IsSuccess).IsTrue().Because(save.Error ?? string.Empty);
        var managedFileDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "docs"));
        var managedFilePath = Path.Combine(managedFileDirectory.FullName, "owned.txt");
        await File.WriteAllTextAsync(managedFilePath, "preserve managed content");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--target", "cyclonedx-1.6", "--output", "docs/./owned.txt"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("conflicts with protected project path");
        await Assert
            .That(await File.ReadAllTextAsync(managedFilePath))
            .IsEqualTo("preserve managed content");
    }

    [Test]
    public async Task Sbom_WhenLockTargetsCanonicalizeToOneFile_RejectsAmbiguousInventory()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var state = new ProjectState
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Sources =
                [
                    new ProjectConfiguration.LocalSource { Name = "local", Path = "source" },
                ],
                Packs = [new ProjectConfiguration.RequestedPack { Id = "owned" }],
            },
            LockFile = new ProjectLockFile
            {
                SchemaVersion = 1,
                Packs =
                [
                    new ProjectLockFile.ResolvedPack
                    {
                        Id = "owned",
                        Version = "1.0.0",
                        PackPath = "owned",
                        SourceName = "local",
                        SourceIdentity = ConfiguredSourceIdentity.CreateLocal("source"),
                        SourcePath = "source",
                        ManagedFiles =
                        [
                            new ProjectLockFile.ManagedFile
                            {
                                DeclaredTargetPath = "docs/owned.txt",
                                TargetPath = "docs/owned.txt",
                                Sha256 = new string('a', 64),
                            },
                            new ProjectLockFile.ManagedFile
                            {
                                DeclaredTargetPath = "docs/owned.txt",
                                TargetPath = "docs/./owned.txt",
                                Sha256 = new string('b', 64),
                            },
                        ],
                    },
                ],
            },
        };
        var save = await workspace.StateStore.SaveAllowingUnavailableSourcesAsync(
            workspace.Path,
            state
        );
        await Assert.That(save.IsSuccess).IsTrue().Because(save.Error ?? string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--target", "cyclonedx-1.7"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("duplicate canonical managed-file target");
    }

    [Test]
    public async Task Sbom_WhenOutputPathEscapesProject_RejectsWithoutWritingOutside()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var outputName = $"lunapack-sbom-{Guid.NewGuid():N}.json";
        var outsidePath = Path.Combine(Directory.GetParent(workspace.Path)!.FullName, outputName);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["sbom", "--target", "cyclonedx-1.6", "--output", $"..\\{outputName}"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEmpty();
        await Assert
            .That(error.ToString())
            .Contains("Path must resolve within the project directory.");
        await Assert.That(File.Exists(outsidePath)).IsFalse();
    }
}
