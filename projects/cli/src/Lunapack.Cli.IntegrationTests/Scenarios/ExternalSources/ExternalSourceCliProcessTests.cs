namespace Lunapack.Cli.IntegrationTests.Scenarios.ExternalSources;

[Property("FileSystem", "Real")]
public sealed class ExternalSourceCliProcessTests
{
    [Test]
    public async Task DirectSource_WhenAccepted_SupportsAuditRenameAndConsumerAwareRemoval()
    {
        using var workspace = new TestWorkspace();
        using var repository = new TestWorkspace();
        await CreateExternalRepositoryAsync(repository.Path, "external content");
        CreateCatalog(workspace.Path, repository.Path, transitive: false);
        await InitializeCatalogAsync(workspace.Path);

        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "external-pack",
            "--accept-sources"
        );
        var installedContent = File.ReadAllText(Path.Combine(workspace.Path, "external.txt"));
        var audit = await CliProcess.InvokeAsync(workspace.Path, "audit");
        var rename = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "rename",
            "upstream",
            "shared"
        );
        var refusedRemoval = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "rm",
            "shared"
        );
        var uninstall = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "external-pack");
        var removal = await CliProcess.InvokeAsync(workspace.Path, "sources", "rm", "shared");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(installedContent).IsEqualTo("external content");
        await Assert.That(audit.ExitCode).IsEqualTo(0);
        await Assert.That(audit.StandardOutput).Contains("upstream");
        await Assert.That(audit.StandardOutput).Contains("refs/heads/main");
        await Assert.That(rename.ExitCode).IsEqualTo(0);
        await Assert.That(refusedRemoval.ExitCode).IsEqualTo(1);
        await Assert.That(uninstall.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "external.txt"))).IsFalse();
        await Assert.That(removal.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task TransitiveSource_WhenConfiguredByFingerprint_ReusesSourceAfterDryRun()
    {
        using var workspace = new TestWorkspace();
        using var repository = new TestWorkspace();
        await CreateExternalRepositoryAsync(repository.Path, "transitive content");
        CreateCatalog(workspace.Path, repository.Path, transitive: true);
        await InitializeCatalogAsync(workspace.Path);
        var source = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "shared",
            repository.Path,
            "--ref",
            "main"
        );
        var configurationBefore = File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml"));
        var lockBefore = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));

        var dryRun = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "root-pack",
            "--dry-run"
        );
        var configurationAfterDryRun = File.ReadAllText(
            Path.Combine(workspace.Path, "lunapack.yml")
        );
        var lockAfterDryRun = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));
        var fileExistsAfterDryRun = File.Exists(Path.Combine(workspace.Path, "external.txt"));
        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "root-pack");
        var configurationAfter = File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml"));

        await Assert.That(source.ExitCode).IsEqualTo(0);
        await Assert.That(dryRun.ExitCode).IsEqualTo(0);
        await Assert.That(configurationAfterDryRun).IsEqualTo(configurationBefore);
        await Assert.That(lockAfterDryRun).IsEqualTo(lockBefore);
        await Assert.That(fileExistsAfterDryRun).IsFalse();
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "external.txt")))
            .IsEqualTo("transitive content");
        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(configurationAfter).Contains("name: shared");
        await Assert.That(configurationAfter).DoesNotContain("name: upstream");
        await Assert
            .That(lockBefore)
            .IsNotEqualTo(File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml")));
    }

    [Test]
    public async Task DirectSource_WhenApprovalUnavailable_PreservesProjectState()
    {
        using var workspace = new TestWorkspace();
        using var repository = new TestWorkspace();
        await CreateExternalRepositoryAsync(repository.Path, "external content");
        CreateCatalog(workspace.Path, repository.Path, transitive: false);
        await InitializeCatalogAsync(workspace.Path);
        var configurationBefore = File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml"));
        var lockBefore = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "external-pack");

        await Assert.That(install.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "external.txt"))).IsFalse();
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml")))
            .IsEqualTo(configurationBefore);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml")))
            .IsEqualTo(lockBefore);
    }

    [Test]
    public async Task DirectSource_WhenIdentifierConflicts_AcceptSourcesPreservesProjectState()
    {
        using var workspace = new TestWorkspace();
        using var repository = new TestWorkspace();
        await CreateExternalRepositoryAsync(repository.Path, "external content");
        CreateCatalog(workspace.Path, repository.Path, transitive: false);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "conflict"));
        await InitializeCatalogAsync(workspace.Path);
        var conflict = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "local",
            "upstream",
            "conflict"
        );
        var configurationBefore = File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml"));

        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "external-pack",
            "--accept-sources"
        );

        await Assert.That(conflict.ExitCode).IsEqualTo(0);
        await Assert.That(install.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "external.txt"))).IsFalse();
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml")))
            .IsEqualTo(configurationBefore);
    }

    [Test]
    public async Task DirectSource_WhenSymbolicRefAdvances_RefreshesAndRejectsLaterDrift()
    {
        using var workspace = new TestWorkspace();
        using var repository = new TestWorkspace();
        await CreateExternalRepositoryAsync(repository.Path, "version one");
        CreateCatalog(workspace.Path, repository.Path, transitive: false);
        await InitializeCatalogAsync(workspace.Path);
        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "external-pack",
            "--accept-sources"
        );
        File.WriteAllText(
            Path.Combine(repository.Path, "standards", "external.txt"),
            "version two"
        );
        await CommitAllAsync(repository.Path, "Update external content");

        var offline = await CliProcess.InvokeAsync(workspace.Path, "outdated", "--offline");
        var outdated = await CliProcess.InvokeAsync(workspace.Path, "outdated");
        var update = await CliProcess.InvokeAsync(workspace.Path, "update", "external-pack");
        var updatedContent = File.ReadAllText(Path.Combine(workspace.Path, "external.txt"));
        var resolvedCommit = (
            await GitProcess.InvokeAsync(repository.Path, "rev-parse", "HEAD")
        ).Trim();
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        var configuration = File.ReadAllText(configurationPath);
        File.WriteAllText(
            configurationPath,
            configuration.Replace("ref: refs/heads/main", $"ref: {resolvedCommit}")
        );

        var driftedUpdate = await CliProcess.InvokeAsync(workspace.Path, "update", "external-pack");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(offline.ExitCode).IsEqualTo(0);
        await Assert.That(offline.StandardOutput).Contains("Remote refs were not checked");
        await Assert.That(outdated.ExitCode).IsEqualTo(0);
        await Assert.That(outdated.StandardOutput).Contains("external source changed");
        await Assert.That(update.ExitCode).IsEqualTo(0);
        await Assert.That(updatedContent).IsEqualTo("version two");
        await Assert.That(driftedUpdate.ExitCode).IsEqualTo(1);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "external.txt")))
            .IsEqualTo("version two");
    }

    private static async Task CreateExternalRepositoryAsync(string repositoryPath, string content)
    {
        var contentDirectory = Directory.CreateDirectory(Path.Combine(repositoryPath, "standards"));
        File.WriteAllText(Path.Combine(contentDirectory.FullName, "external.txt"), content);
        await GitProcess.InvokeAsync(repositoryPath, "init", "--initial-branch=main");
        await CommitAllAsync(repositoryPath, "Initial external content");
    }

    private static async Task CommitAllAsync(string repositoryPath, string message)
    {
        await GitProcess.InvokeAsync(repositoryPath, "add", ".");
        await GitProcess.InvokeAsync(
            repositoryPath,
            "-c",
            "user.email=lunapack@example.test",
            "-c",
            "user.name=Lunapack Test",
            "commit",
            "--quiet",
            "-m",
            message
        );
    }

    private static void CreateCatalog(string workspacePath, string repositoryPath, bool transitive)
    {
        var catalog = Directory.CreateDirectory(Path.Combine(workspacePath, "catalog"));
        if (transitive)
        {
            var root = Directory.CreateDirectory(Path.Combine(catalog.FullName, "root"));
            File.WriteAllText(
                Path.Combine(root.FullName, "pack.yml"),
                "id: root-pack\nversion: 1.0.0\nlicense: MIT\nauthor: LunaPack Tests\npacks:\n  - id: external-pack\n    version: 1.0.0\n"
            );
        }

        var pack = Directory.CreateDirectory(Path.Combine(catalog.FullName, "external"));
        File.WriteAllText(
            Path.Combine(pack.FullName, "pack.yml"),
            $"id: external-pack\nversion: 1.0.0\nlicense: MIT\nauthor: LunaPack Tests\nsources:\n  upstream:\n    type: git\n    url: '{repositoryPath}'\n    ref: refs/heads/main\nmanagedFiles:\n  - source: upstream\n    path: standards/external.txt\n    target: external.txt\n"
        );
    }

    private static async Task InitializeCatalogAsync(string workspacePath)
    {
        var initialization = await CliProcess.InvokeAsync(workspacePath, "init");
        var source = await CliProcess.InvokeAsync(
            workspacePath,
            "sources",
            "add",
            "local",
            "catalog",
            "catalog"
        );

        if (initialization.ExitCode != 0 || source.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to initialize integration fixture: {initialization.StandardOutput}{source.StandardOutput}"
            );
        }
    }
}
