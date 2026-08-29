using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Links;

public sealed class LinkLifecycleServiceTests
{
    private static readonly string ProjectDirectory = OperatingSystem.IsWindows()
        ? @"C:\project"
        : "/project";

    [Test]
    public async Task InstallAsync_WhenLinkIsConfigured_WritesTargetsAndLockState()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());

        var exitCode = await service.InstallAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(fileSystem.File.Exists(TargetPath("CSharpExpert.agent.md"))).IsTrue();
        var lockFile = await LoadLockFileAsync(fileSystem);
        await Assert.That(lockFile.Links.ContainsKey("agents")).IsTrue();
        await Assert
            .That(lockFile.Links["agents"].Files.Select(file => file.TargetPath))
            .IsEquivalentTo([
                ".github/agents/CSharpExpert.agent.md",
                ".github/agents/ai-team.agent.md",
            ]);
    }

    [Test]
    public async Task InstallAsync_WhenLinkIsNotConfigured_Fails()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());

        var exitCode = await service.InstallAsync(ProjectDirectory, "missing");

        await Assert.That(exitCode).IsEqualTo(1);
    }

    [Test]
    public async Task InstallAsync_WhenLinkIsAlreadyInstalled_Fails()
    {
        var fileSystem = CreateFileSystem();
        var console = new SpectreTestConsole();
        var service = CreateService(fileSystem, console);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");

        var exitCode = await service.InstallAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(Unwrap(console)).Contains("is already installed");
    }

    [Test]
    public async Task InstallAsync_WhenTargetExistsUnmanaged_FailsWithoutAdoption()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.AddFile(TargetPath("CSharpExpert.agent.md"), new MockFileData("local"));
        var console = new SpectreTestConsole();
        var service = CreateService(fileSystem, console);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());

        var exitCode = await service.InstallAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(Unwrap(console)).Contains("is not managed by LunaPack");
    }

    [Test]
    public async Task InstallAsync_WhenTargetMatchesAndAdoptionIsRequested_Succeeds()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.AddFile(TargetPath("CSharpExpert.agent.md"), new MockFileData("expert"));
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());

        var exitCode = await service.InstallAsync(ProjectDirectory, "agents", adoptExisting: true);

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task InstallAsync_WhenTargetIsOwnedByPack_ReportsOwnershipConflict()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.AddFile(TargetPath("CSharpExpert.agent.md"), new MockFileData("expert"));
        var console = new SpectreTestConsole();
        var service = CreateService(fileSystem, console);
        var configuration = CreateConfiguration();
        configuration.Packs = [new ProjectConfiguration.RequestedPack { Id = "acme-pack" }];
        await WriteStateAsync(
            fileSystem,
            configuration,
            new ProjectLockFile
            {
                SchemaVersion = 1,
                Packs = [CreateResolvedPack(".github/agents/CSharpExpert.agent.md")],
            }
        );

        var exitCode = await service.InstallAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(Unwrap(console)).Contains("already managed by pack 'acme-pack'");
    }

    [Test]
    public async Task UpdateAsync_WhenSourceContentChanges_RewritesTargetAndLockDigest()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");
        fileSystem.File.WriteAllText(SourceFilePath("agents", "CSharpExpert.agent.md"), "changed");

        var exitCode = await service.UpdateAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(fileSystem.File.ReadAllText(TargetPath("CSharpExpert.agent.md")))
            .IsEqualTo("changed");
    }

    [Test]
    public async Task UpdateAsync_WhenSelectionShrinks_RemovesOrphanedTarget()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");
        fileSystem.File.Delete(SourceFilePath("agents", "ai-team.agent.md"));

        var exitCode = await service.UpdateAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(fileSystem.File.Exists(TargetPath("ai-team.agent.md"))).IsFalse();
        var lockFile = await LoadLockFileAsync(fileSystem);
        await Assert.That(lockFile.Links["agents"].Files.Count).IsEqualTo(1);
    }

    [Test]
    public async Task UpdateAsync_WhenTargetDirectoryBecomesIgnored_PreservesFilesAndDropsOwnership()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        var configuration = CreateConfiguration();
        await WriteConfigurationAsync(fileSystem, configuration);
        await service.InstallAsync(ProjectDirectory, "agents");
        configuration.Remap = new ProjectConfiguration.Remapping
        {
            Directories = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".github/agents"] = ManagedFileTargetRemapping.IgnoreTarget,
            },
        };
        await WriteConfigurationAsync(fileSystem, configuration);

        var exitCode = await service.UpdateAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(fileSystem.File.Exists(TargetPath("CSharpExpert.agent.md"))).IsTrue();
        await Assert.That(fileSystem.File.Exists(TargetPath("ai-team.agent.md"))).IsTrue();
        var lockFile = await LoadLockFileAsync(fileSystem);
        await Assert.That(lockFile.Links["agents"].Files).IsEmpty();
    }

    [Test]
    public async Task UpdateAsync_WhenNothingChanged_KeepsLockRecord()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");
        var before = await LoadLockFileAsync(fileSystem);

        var exitCode = await service.UpdateAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(0);
        var after = await LoadLockFileAsync(fileSystem);
        await Assert
            .That(after.Links["agents"].DefinitionSha256)
            .IsEqualTo(before.Links["agents"].DefinitionSha256);
    }

    [Test]
    public async Task OutdatedAsync_WhenDefinitionChanges_ReportsReason()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");
        var configuration = CreateConfiguration();
        configuration.Links["agents"].Includes = ["CSharpExpert.agent.md"];
        await WriteConfigurationAsync(fileSystem, configuration);

        var reports = (await service.OutdatedAsync(ProjectDirectory)).RequireValue();

        await Assert.That(reports.Count).IsEqualTo(1);
        await Assert.That(reports[0].Name).IsEqualTo("agents");
        await Assert.That(reports[0].Reasons).Contains("definition changed");
        await Assert.That(reports[0].Reasons).Contains("files removed");
    }

    [Test]
    public async Task OutdatedAsync_WhenLinkIsCurrent_ReportsNothing()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");

        var reports = (await service.OutdatedAsync(ProjectDirectory)).RequireValue();

        await Assert.That(reports).IsEmpty();
    }

    [Test]
    public async Task OutdatedAsync_WhenLinkIsNotInstalled_ReportsNotInstalled()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());

        var reports = (await service.OutdatedAsync(ProjectDirectory)).RequireValue();

        await Assert.That(reports[0].Reasons).IsEquivalentTo(["not installed"]);
    }

    [Test]
    public async Task Audit_WhenTargetsAreMissingOrModified_ReportsStatuses()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");
        fileSystem.File.Delete(TargetPath("ai-team.agent.md"));
        fileSystem.File.WriteAllText(TargetPath("CSharpExpert.agent.md"), "edited");

        var reports = service.Audit(ProjectDirectory, await LoadLockFileAsync(fileSystem));

        await Assert.That(reports.Count).IsEqualTo(1);
        await Assert
            .That(reports[0].Files.Select(file => $"{file.TargetPath}={file.Status}"))
            .IsEquivalentTo([
                ".github/agents/CSharpExpert.agent.md=modified",
                ".github/agents/ai-team.agent.md=missing",
            ]);
    }

    [Test]
    public async Task Audit_WhenPackClaimsTheSameTarget_ReportsConflict()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");
        var lockFile = await LoadLockFileAsync(fileSystem);
        lockFile.Packs.Add(CreateResolvedPack(".github/agents/ai-team.agent.md"));

        var reports = service.Audit(ProjectDirectory, lockFile);

        await Assert
            .That(reports[0].Files.Select(file => file.Status))
            .Contains("conflicting", StringComparer.Ordinal);
    }

    [Test]
    public async Task UninstallAsync_WhenTargetsAreUnchanged_PreservesDefinitionAndRemovesTargets()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");

        var exitCode = await service.UninstallAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(fileSystem.File.Exists(TargetPath("CSharpExpert.agent.md"))).IsFalse();
        var lockFile = await LoadLockFileAsync(fileSystem);
        await Assert.That(lockFile.Links).IsEmpty();
        var configuration = await LoadConfigurationAsync(fileSystem);
        await Assert.That(configuration.Links.ContainsKey("agents")).IsTrue();
    }

    [Test]
    public async Task UninstallAsync_WhenTargetIsModified_PreservesAllState()
    {
        var fileSystem = CreateFileSystem();
        var console = new SpectreTestConsole();
        var service = CreateService(fileSystem, console);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");
        fileSystem.File.WriteAllText(TargetPath("CSharpExpert.agent.md"), "edited");

        var exitCode = await service.UninstallAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(Unwrap(console)).Contains("has changed");
        await Assert.That(fileSystem.File.Exists(TargetPath("ai-team.agent.md"))).IsTrue();
        var lockFile = await LoadLockFileAsync(fileSystem);
        await Assert.That(lockFile.Links.ContainsKey("agents")).IsTrue();
    }

    [Test]
    public async Task UninstallAsync_WhenLinkIsNotInstalled_Fails()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());

        var exitCode = await service.UninstallAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(1);
    }

    [Test]
    public async Task RemoveAsync_WhenLinkIsInstalledWithoutForce_Refuses()
    {
        var fileSystem = CreateFileSystem();
        var console = new SpectreTestConsole();
        var service = CreateService(fileSystem, console);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");

        var exitCode = await service.RemoveAsync(ProjectDirectory, "agents", force: false);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(Unwrap(console)).Contains("--force");
        await Assert.That(fileSystem.File.Exists(TargetPath("CSharpExpert.agent.md"))).IsTrue();
    }

    [Test]
    public async Task RemoveAsync_WhenForced_DeletesUnchangedTargetsAndPreservesModifiedTargets()
    {
        var fileSystem = CreateFileSystem();
        var console = new SpectreTestConsole();
        var service = CreateService(fileSystem, console);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");
        fileSystem.File.WriteAllText(TargetPath("CSharpExpert.agent.md"), "edited");

        var exitCode = await service.RemoveAsync(ProjectDirectory, "agents", force: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(fileSystem.File.Exists(TargetPath("CSharpExpert.agent.md"))).IsTrue();
        await Assert.That(fileSystem.File.Exists(TargetPath("ai-team.agent.md"))).IsFalse();
        await Assert.That(Unwrap(console)).Contains("Preserved locally modified target");
        var lockFile = await LoadLockFileAsync(fileSystem);
        await Assert.That(lockFile.Links).IsEmpty();
        var configuration = await LoadConfigurationAsync(fileSystem);
        await Assert.That(configuration.Links).IsEmpty();
    }

    [Test]
    public async Task RemoveAsync_WhenLinkIsNotInstalled_RemovesDefinitionOnly()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());

        var exitCode = await service.RemoveAsync(ProjectDirectory, "agents", force: false);

        await Assert.That(exitCode).IsEqualTo(0);
        var configuration = await LoadConfigurationAsync(fileSystem);
        await Assert.That(configuration.Links).IsEmpty();
    }

    [Test]
    public async Task InstallAsync_WhenTargetIsDeletedAfterInstall_RestoresTarget()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");
        fileSystem.File.Delete(TargetPath("ai-team.agent.md"));

        var exitCode = await service.InstallAsync(ProjectDirectory, "agents", allowReinstall: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(fileSystem.File.Exists(TargetPath("ai-team.agent.md"))).IsTrue();
    }

    [Test]
    public async Task UpdateAsync_WhenSourceIsMissing_Fails()
    {
        var fileSystem = CreateFileSystem();
        var console = new SpectreTestConsole();
        var service = CreateService(fileSystem, console);
        await WriteConfigurationAsync(fileSystem, CreateConfiguration());
        await service.InstallAsync(ProjectDirectory, "agents");
        var configuration = CreateConfiguration();
        configuration.Sources =
        [
            new ProjectConfiguration.LocalSource { Name = "upstream", Path = "other" },
        ];
        await WriteConfigurationAsync(fileSystem, configuration, allowUnconfiguredSources: true);
        fileSystem.AddFile(
            Path.Combine(ProjectDirectory, "other", "agents", "CSharpExpert.agent.md"),
            new MockFileData("expert")
        );

        var exitCode = await service.UpdateAsync(ProjectDirectory, "agents");

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(Unwrap(console)).Contains("locked identity");
    }

    private static LinkLifecycleService CreateService(
        IFileSystem fileSystem,
        SpectreTestConsole? ansiConsole = null
    )
    {
        var console = new CliConsole(ansiConsole ?? new SpectreTestConsole(), CliLogLevel.Info);
        return new LinkLifecycleService(
            fileSystem,
            new LinkResolver(
                fileSystem,
                new LinkTargetMapper(fileSystem),
                [new LocalLinkSourceProvider(fileSystem)]
            ),
            new LinkPlanner(fileSystem),
            new PackUpdateTransaction(fileSystem, console),
            new ProjectStateStore(fileSystem),
            console
        );
    }

    private static MockFileSystem CreateFileSystem()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(
            SourceFilePath("agents", "CSharpExpert.agent.md"),
            new MockFileData("expert")
        );
        fileSystem.AddFile(SourceFilePath("agents", "ai-team.agent.md"), new MockFileData("team"));
        return fileSystem;
    }

    private static ProjectConfiguration CreateConfiguration() =>
        new()
        {
            SchemaVersion = 1,
            Links =
            {
                ["agents"] = new ProjectConfiguration.Link
                {
                    Includes = ["**/*.agent.md"],
                    Path = "agents",
                    Source = "upstream",
                    Target = ".github/agents",
                },
            },
            Sources =
            [
                new ProjectConfiguration.LocalSource { Name = "upstream", Path = "upstream" },
            ],
        };

    private static ProjectLockFile.ResolvedPack CreateResolvedPack(string targetPath) =>
        new()
        {
            Id = "acme-pack",
            PackPath = "packs/acme",
            SourceIdentity = ConfiguredSourceIdentity.CreateLocal("upstream"),
            SourceName = "upstream",
            SourcePath = "upstream",
            Version = "1.0.0",
            ManagedFiles =
            [
                new ProjectLockFile.ManagedFile
                {
                    DeclaredTargetPath = targetPath,
                    Sha256 = new string('a', 64),
                    TargetPath = targetPath,
                },
            ],
        };

    private static string Unwrap(SpectreTestConsole console) =>
        string.Join(
            ' ',
            console
                .Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
        );

    private static async Task WriteConfigurationAsync(
        IFileSystem fileSystem,
        ProjectConfiguration configuration,
        bool allowUnconfiguredSources = false
    )
    {
        var state = await new ProjectStateStore(fileSystem).LoadAsync(ProjectDirectory);
        await WriteStateAsync(
            fileSystem,
            configuration,
            state.Value?.LockFile ?? new ProjectLockFile { SchemaVersion = 1 },
            allowUnconfiguredSources
        );
    }

    private static async Task WriteStateAsync(
        IFileSystem fileSystem,
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        bool allowUnconfiguredSources = false
    )
    {
        var store = new ProjectStateStore(fileSystem);
        var nextState = new ProjectState { Configuration = configuration, LockFile = lockFile };
        var saved = allowUnconfiguredSources
            ? await store.SaveAllowingUnavailableSourcesAsync(ProjectDirectory, nextState)
            : await store.SaveAsync(ProjectDirectory, nextState);
        if (!saved.IsSuccess)
        {
            throw new InvalidOperationException(saved.Error);
        }
    }

    private static async Task<ProjectLockFile> LoadLockFileAsync(IFileSystem fileSystem) =>
        (await new ProjectStateStore(fileSystem).LoadAsync(ProjectDirectory))
            .RequireValue()
            .LockFile;

    private static async Task<ProjectConfiguration> LoadConfigurationAsync(
        IFileSystem fileSystem
    ) =>
        (await new ProjectStateStore(fileSystem).LoadAsync(ProjectDirectory))
            .RequireValue()
            .Configuration;

    private static string SourceFilePath(params string[] segments) =>
        Path.Combine([ProjectDirectory, "upstream", .. segments]);

    private static string TargetPath(string fileName) =>
        Path.Combine(ProjectDirectory, ".github", "agents", fileName);
}
