using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests.Packs.ExternalSources;

public sealed class ExternalPackLifecycleTests
{
    [Test]
    public async Task Install_WhenExternalSourceAccepted_CommitsSourceFileAndProvenance()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        var console = new Spectre.Console.Testing.TestConsole();
        using var workspace = await CreateWorkspaceAsync(runner, console);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );
        if (exitCode != 0)
        {
            throw new InvalidOperationException(console.Output);
        }

        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).Value;
        var lockPack = state?.LockFile.Packs.Single();
        var lockedFile = lockPack?.ManagedFiles.Single();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "README.md")))
            .IsEqualTo("external content");
        await Assert
            .That(
                state?.Configuration.Sources.Any(source =>
                    string.Equals(source.Name, "upstream", StringComparison.Ordinal)
                )
            )
            .IsTrue();
        await Assert.That(lockPack?.ExternalSources["upstream"].SourceName).IsEqualTo("upstream");
        await Assert.That(lockedFile?.SourceAlias).IsEqualTo("upstream");
        await Assert.That(lockedFile?.SourcePath).IsEqualTo("README.md");
    }

    [Test]
    public async Task Install_WhenExternalSourceApprovalUnavailable_PreservesProjectState()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        using var workspace = await CreateWorkspaceAsync(runner);
        var originalConfiguration = File.ReadAllText(
            Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName)
        );
        var originalLock = File.ReadAllText(
            Path.Combine(workspace.Path, ProjectStateStore.LockFileName)
        );

        var exitCode = await workspace.Application.RunAsync(["install", "example"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(
                File.ReadAllText(
                    Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName)
                )
            )
            .IsEqualTo(originalConfiguration);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ProjectStateStore.LockFileName)))
            .IsEqualTo(originalLock);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "README.md"))).IsFalse();
    }

    [Test]
    public async Task InstallDryRun_WhenExternalSourceApprovalUnavailable_ReportsPlanWithoutMutation()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        var console = new Spectre.Console.Testing.TestConsole();
        using var workspace = await CreateWorkspaceAsync(runner, console);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var lockPath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var originalConfiguration = File.ReadAllText(configurationPath);
        var originalLock = File.ReadAllText(lockPath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "example", "--dry-run"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("Map  example: upstream -> upstream");
        await Assert.That(console.Output).Contains("Add  upstream");
        await Assert.That(console.Output).Contains("approval required");
        await Assert.That(console.Output).Contains("Create  README.md");
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(originalConfiguration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(originalLock);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "README.md"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenExternalGitCheckoutFails_PreservesProjectState()
    {
        var runner = new ExternalCheckoutGitProcessRunner(failCheckout: true);
        using var workspace = await CreateWorkspaceAsync(runner);
        var originalConfiguration = File.ReadAllText(
            Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName)
        );

        var exitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).Value;

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(
                File.ReadAllText(
                    Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName)
                )
            )
            .IsEqualTo(originalConfiguration);
        await Assert.That(state?.LockFile.Packs).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "README.md"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenExternalSourceIdentifierSelectionCanceled_PreservesProjectState()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        using var workspace = await CreateWorkspaceAsync(runner);
        var sourceExitCode = await workspace.Application.RunAsync(
            [
                "sources",
                "add",
                "git",
                "upstream",
                "https://github.com/example/other.git",
                "--ref",
                "main",
            ],
            workspace.Path
        );
        await Assert.That(sourceExitCode).IsEqualTo(0);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var lockPath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var originalConfiguration = File.ReadAllText(configurationPath);
        var originalLock = File.ReadAllText(lockPath);
        var service = CreateLifecycleService(
            workspace,
            runner,
            workspace.StateStore,
            new ExternalSourceConsentCoordinator(
                new StubExternalSourceApprover(true),
                new StubExternalSourceIdentifierPrompter(null)
            )
        );

        var exitCode = await service.InstallAsync(
            workspace.Path,
            new PackInstallationRequest(new PackReference("example", null), null, false)
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(originalConfiguration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(originalLock);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "README.md"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenExternalSelectorMatchesNothing_PreservesProjectState()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        using var workspace = await CreateWorkspaceAsync(runner);
        var manifestPath = Path.Combine(
            workspace.Path,
            "packs",
            "example",
            PackManifestStore.FileName
        );
        File.WriteAllText(
            manifestPath,
            File.ReadAllText(manifestPath)
                .Replace("path: README.md", "path: missing.md", StringComparison.Ordinal)
        );
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var lockPath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var originalConfiguration = File.ReadAllText(configurationPath);
        var originalLock = File.ReadAllText(lockPath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(originalConfiguration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(originalLock);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "README.md"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenExternalTargetConflicts_PreservesProjectState()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        using var workspace = await CreateWorkspaceAsync(runner);
        var targetPath = Path.Combine(workspace.Path, "README.md");
        File.WriteAllText(targetPath, "workspace content");
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var lockPath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var originalConfiguration = File.ReadAllText(configurationPath);
        var originalLock = File.ReadAllText(lockPath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("workspace content");
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(originalConfiguration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(originalLock);
    }

    [Test]
    public async Task Install_WhenProjectStateWriteFails_RollsBackExternalTargetAndSource()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        using var workspace = await CreateWorkspaceAsync(runner);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        var lockPath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var originalConfiguration = File.ReadAllText(configurationPath);
        var originalLock = File.ReadAllText(lockPath);
        var service = CreateLifecycleService(
            workspace,
            runner,
            new FailingProjectStateStore(workspace.StateStore),
            new ExternalSourceConsentCoordinator(
                new StubExternalSourceApprover(true),
                new StubExternalSourceIdentifierPrompter(null)
            )
        );

        var exitCode = await service.InstallAsync(
            workspace.Path,
            new PackInstallationRequest(new PackReference("example", null), null, false)
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(originalConfiguration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(originalLock);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "README.md"))).IsFalse();
    }

    [Test]
    public async Task Update_WhenExternalSourceFingerprintDrifts_PreservesProjectState()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        using var workspace = await CreateWorkspaceAsync(runner);
        var installExitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );
        await Assert.That(installExitCode).IsEqualTo(0);
        var configurationPath = Path.Combine(
            workspace.Path,
            ProjectStateStore.ConfigurationFileName
        );
        File.WriteAllText(
            configurationPath,
            File.ReadAllText(configurationPath)
                .Replace("example/standards.git", "example/other.git", StringComparison.Ordinal)
        );
        var lockPath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var driftedConfiguration = File.ReadAllText(configurationPath);
        var originalLock = File.ReadAllText(lockPath);
        var output = new Spectre.Console.Testing.TestConsole();

        var exitCode = await CreateLifecycleService(
                workspace,
                runner,
                workspace.StateStore,
                new ExternalSourceConsentCoordinator(
                    new StubExternalSourceApprover(true),
                    new StubExternalSourceIdentifierPrompter(null)
                ),
                new CliConsole(output, CliLogLevel.Info)
            )
            .UpdateAsync(
                workspace.Path,
                [new ProjectConfiguration.RequestedPack { Id = "example", Version = "1.0.0" }],
                new PackInstallationRequest(new PackReference("example", "1.0.0"), null, false)
            );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(driftedConfiguration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(originalLock);
        await Assert.That(output.Output).Contains("locked external source");
        await Assert.That(output.Output).Contains("configured");
        await Assert.That(output.Output).Contains("source 'upstream'");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "README.md")))
            .IsEqualTo("external content");
    }

    [Test]
    public async Task Update_WhenExternalSymbolicRefContentChanges_RefreshesUnchangedPackVersion()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        using var workspace = await CreateWorkspaceAsync(runner);
        var installExitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );
        await Assert.That(installExitCode).IsEqualTo(0);
        runner.ResolvedCommit = "2222222222222222222222222222222222222222";
        runner.Content = "updated external content";

        var exitCode = await workspace.Application.RunAsync(["update", "example"], workspace.Path);
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).Value;

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "README.md")))
            .IsEqualTo("updated external content");
        await Assert.That(state?.LockFile.Packs.Single().Version).IsEqualTo("1.0.0");
        await Assert
            .That(state?.LockFile.Packs.Single().ExternalSources["upstream"].ResolvedCommit)
            .IsEqualTo(runner.ResolvedCommit);
    }

    [Test]
    public async Task Update_WhenExternalRequirementRemoved_RemovesConsumerAndRetainsSource()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        using var workspace = await CreateWorkspaceAsync(runner);
        var installExitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );
        await Assert.That(installExitCode).IsEqualTo(0);
        File.WriteAllText(
            Path.Combine(workspace.Path, "packs", "example", PackManifestStore.FileName),
            "id: example\nversion: 1.0.0\nauthor: Example\nlicense: MIT\n"
        );

        var exitCode = await workspace.Application.RunAsync(["update", "example"], workspace.Path);
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).Value;

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "README.md"))).IsFalse();
        await Assert.That(state?.LockFile.Packs.Single().ExternalSources).IsEmpty();
        await Assert
            .That(
                state?.Configuration.Sources.Any(source =>
                    string.Equals(source.Name, "upstream", StringComparison.Ordinal)
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task Outdated_WhenExternalContentChanges_ReportsReasonAndHonorsOfflineMode()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        var console = new Spectre.Console.Testing.TestConsole();
        console.Profile.Width = 200;
        using var workspace = await CreateWorkspaceAsync(runner, console);
        var installExitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );
        await Assert.That(installExitCode).IsEqualTo(0);
        runner.ResolvedCommit = "2222222222222222222222222222222222222222";

        var unchangedExitCode = await workspace.Application.RunAsync(["outdated"], workspace.Path);

        await Assert.That(unchangedExitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("No updates are available");
        runner.Content = "updated external content";

        var outdatedExitCode = await workspace.Application.RunAsync(["outdated"], workspace.Path);

        await Assert.That(outdatedExitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("external source changed");
        var remoteCallsBeforeOffline = runner.RemoteCallCount;

        var offlineExitCode = await workspace.Application.RunAsync(
            ["outdated", "--offline"],
            workspace.Path
        );

        await Assert.That(offlineExitCode).IsEqualTo(0);
        await Assert.That(runner.RemoteCallCount).IsEqualTo(remoteCallsBeforeOffline);
        await Assert.That(console.Output).Contains("Remote refs were not checked");
    }

    [Test]
    public async Task Audit_WhenExternalTargetModified_ReportsProvenanceAndStatus()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        var console = new Spectre.Console.Testing.TestConsole();
        console.Profile.Width = 240;
        using var workspace = await CreateWorkspaceAsync(runner, console);
        var installExitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );
        await Assert.That(installExitCode).IsEqualTo(0);
        File.WriteAllText(Path.Combine(workspace.Path, "README.md"), "locally modified");

        var exitCode = await workspace.Application.RunAsync(["audit"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("example@1.0.0");
        await Assert.That(console.Output).Contains("alias=upstream");
        await Assert.That(console.Output).Contains("workspace=upstream");
        await Assert.That(console.Output).Contains("github.com/example/standards");
        await Assert.That(console.Output).Contains("ref=refs/heads/main");
        await Assert.That(console.Output).Contains($"commit={runner.ResolvedCommit}");
        await Assert.That(console.Output).Contains("source=README.md");
        await Assert.That(console.Output).Contains("target=README.md");
        await Assert.That(console.Output).Contains("status=locally modified");
    }

    [Test]
    public async Task Uninstall_WhenExternalTargetModified_PreservesTargetAndOwnership()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        using var workspace = await CreateWorkspaceAsync(runner);
        var installExitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );
        await Assert.That(installExitCode).IsEqualTo(0);
        var targetPath = Path.Combine(workspace.Path, "README.md");
        File.WriteAllText(targetPath, "locally modified");

        var exitCode = await workspace.Application.RunAsync(
            ["uninstall", "example"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).Value;

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("locally modified");
        await Assert.That(state?.LockFile.Packs.Single().Id).IsEqualTo("example");
    }

    [Test]
    public async Task Uninstall_WhenLastExternalConsumerRemoved_SuggestsExplicitSourceCleanup()
    {
        var runner = new ExternalCheckoutGitProcessRunner();
        var console = new Spectre.Console.Testing.TestConsole();
        console.Profile.Width = 200;
        using var workspace = await CreateWorkspaceAsync(runner, console);
        var installExitCode = await workspace.Application.RunAsync(
            ["install", "example", "--accept-sources"],
            workspace.Path
        );
        await Assert.That(installExitCode).IsEqualTo(0);

        var exitCode = await workspace.Application.RunAsync(
            ["uninstall", "example"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("luna sources rm upstream");
    }

    private static async Task<TestWorkspace> CreateWorkspaceAsync(
        IGitProcessRunner runner,
        Spectre.Console.Testing.TestConsole? console = null
    )
    {
        var workspace = new TestWorkspace(ansiConsole: console, gitProcessRunner: runner);
        var initExit = await workspace.Application.RunAsync(["init"], workspace.Path);
        if (initExit != 0)
        {
            workspace.Dispose();
            throw new InvalidOperationException("Unable to initialize test workspace.");
        }

        var packDirectory = Path.Combine(workspace.Path, "packs", "example");
        Directory.CreateDirectory(packDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, PackManifestStore.FileName),
            """
            id: example
            version: 1.0.0
            author: Example
            license: MIT
            sources:
              upstream:
                type: git
                url: https://github.com/example/standards.git
                ref: refs/heads/main
            managedFiles:
              - source: upstream
                path: README.md
                target: README.md
            """
        );
        var sourceExit = await workspace.Application.RunAsync(
            ["sources", "add", "local", "catalog", "packs"],
            workspace.Path
        );
        if (sourceExit != 0)
        {
            workspace.Dispose();
            throw new InvalidOperationException("Unable to add test pack source.");
        }

        return workspace;
    }

    private static PackLifecycleService CreateLifecycleService(
        TestWorkspace workspace,
        IGitProcessRunner runner,
        IProjectStateStore stateStore,
        ExternalSourceConsentCoordinator consentCoordinator,
        CliConsole? configuredConsole = null
    )
    {
        var console = configuredConsole ?? TestConsole.Create();
        var refResolver = new GitRefResolver(runner);
        var packCatalog = new PackCatalog(workspace.FileSystem, console, runner);
        return new PackLifecycleService(
            workspace.FileSystem,
            new CompositePackGraphResolver(packCatalog),
            new PackInstallationPlanner(
                workspace.FileSystem,
                new PackTemplateRenderer(workspace.FileSystem)
            ),
            new PackUpdatePlanner(workspace.FileSystem),
            new PackUpdateTransaction(workspace.FileSystem, console),
            stateStore,
            console,
            configuredGitPackMaterializer: new GitPackMaterializer(workspace.FileSystem, runner),
            configuredExternalSourceRequirementPlanner: new ExternalSourceRequirementPlanner(
                refResolver
            ),
            configuredExternalSourceMaterializer: new ExternalSourceMaterializer(
                workspace.FileSystem,
                runner,
                refResolver
            ),
            configuredExternalSourceConsentCoordinator: consentCoordinator
        );
    }

    private sealed class StubExternalSourceApprover(bool result) : IExternalSourceApprover
    {
        public Task<bool> ApproveAsync(
            IReadOnlyList<ExternalSourceRequirementGroup> sources,
            CancellationToken cancellationToken
        ) => Task.FromResult(result);
    }

    private sealed class StubExternalSourceIdentifierPrompter(string? result)
        : IExternalSourceIdentifierPrompter
    {
        public Task<string?> PromptAsync(
            ExternalSourceRequirementGroup source,
            string conflictingIdentifier,
            CancellationToken cancellationToken
        ) => Task.FromResult(result);
    }

    private sealed class ExternalCheckoutGitProcessRunner(bool failCheckout = false)
        : IGitProcessRunner
    {
        public string Content { get; set; } = "external content";

        public string ResolvedCommit { get; set; } = "1111111111111111111111111111111111111111";

        public int RemoteCallCount { get; private set; }

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            if (
                arguments.Count > 0
                && string.Equals(arguments[0], "ls-remote", StringComparison.Ordinal)
            )
            {
                RemoteCallCount++;
                return Success($"{ResolvedCommit}\trefs/heads/main");
            }

            if (arguments.Contains("checkout", StringComparer.Ordinal))
            {
                if (failCheckout)
                {
                    return Task.FromResult(
                        ManifestOperationResult<GitProcessOutput>.Failure(
                            "External checkout failed."
                        )
                    );
                }

                File.WriteAllText(Path.Combine(arguments[1], "README.md"), Content);
            }

            return Success(string.Empty);
        }

        private static Task<ManifestOperationResult<GitProcessOutput>> Success(string output) =>
            Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(output, string.Empty)
                )
            );
    }
}
