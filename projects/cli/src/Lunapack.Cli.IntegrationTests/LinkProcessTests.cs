namespace Lunapack.Cli.IntegrationTests;

[Property("FileSystem", "Real")]
public sealed class LinkProcessTests
{
    [Test]
    public async Task LocalLink_WhenExactFileSelected_InstallsContentAndLockProvenance()
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory.FullName, "agents"));
        File.WriteAllText(
            Path.Combine(sourceDirectory.FullName, "agents", "expert.agent.md"),
            "agent content"
        );
        await CliProcess.InvokeAsync(workspace.Path, "init");
        await CliProcess.InvokeAsync(workspace.Path, "sources", "add", "local", "local", "source");

        var result = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "agents-expert",
            "--source",
            "local",
            "--include",
            "agents/expert.agent.md",
            "--target",
            ".github/agents",
            "--install"
        );

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert
            .That(
                File.ReadAllText(
                    Path.Combine(workspace.Path, ".github", "agents", "agents", "expert.agent.md")
                )
            )
            .IsEqualTo("agent content");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml")))
            .Contains("sourcePath: agents/expert.agent.md")
            .And.Contains("targetPath: .github/agents/agents/expert.agent.md");
    }

    [Test]
    public async Task LocalLink_WhenInstalledWithDirectoryRemapping_WritesRemappedTarget()
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory.FullName, "agents"));
        File.WriteAllText(
            Path.Combine(sourceDirectory.FullName, "agents", "expert.agent.md"),
            "agent content"
        );
        await InitializeLocalSourceAsync(workspace.Path);
        await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "csharp-agent",
            "--source",
            "local",
            "--include",
            "agents/expert.agent.md"
        );

        var result = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "csharp-agent",
            "--remap-directory",
            "agents/=.github/agents"
        );

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, ".github", "agents", "expert.agent.md")))
            .IsTrue();
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "agents", "expert.agent.md")))
            .IsFalse();
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml")))
            .Contains("declaredTargetPath: agents/expert.agent.md")
            .And.Contains("targetPath: .github/agents/expert.agent.md");
    }

    [Test]
    public async Task LocalLink_WhenInstallRemappingSaved_ReusesMappingOnFutureInstall()
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory.FullName, "agents"));
        File.WriteAllText(
            Path.Combine(sourceDirectory.FullName, "agents", "expert.agent.md"),
            "agent content"
        );
        await InitializeLocalSourceAsync(workspace.Path);
        await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "csharp-agent",
            "--source",
            "local",
            "--include",
            "agents/expert.agent.md"
        );

        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "csharp-agent",
            "--remap-directory",
            "agents=.github/agents",
            "--save-remap"
        );
        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert
            .That(
                (await CliProcess.InvokeAsync(workspace.Path, "uninstall", "csharp-agent")).ExitCode
            )
            .IsEqualTo(0);

        var reinstall = await CliProcess.InvokeAsync(workspace.Path, "install", "csharp-agent");

        await Assert.That(reinstall.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, ".github", "agents", "expert.agent.md")))
            .IsTrue();
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml")))
            .Contains("agents: .github/agents");
    }

    [Test]
    public async Task LocalLink_WhenSelectorsAndMappingsCombined_InstallsDeterministicSelection()
    {
        using var workspace = new TestWorkspace();
        CreateSelectionSource(workspace.Path);
        await InitializeLocalSourceAsync(workspace.Path);

        var selected = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "selected-content",
            "--source",
            "local",
            "--path",
            "content",
            "--include",
            "docs",
            "--include",
            "**/*.txt",
            "--exclude",
            "**/skip.md",
            "--target",
            "generated",
            "--install"
        );
        var flattened = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "flattened-content",
            "--source",
            "local",
            "--path",
            "content",
            "--include",
            "prefix/**/*.md",
            "--strip-prefix",
            "prefix",
            "--target",
            "flat",
            "--flatten",
            "--install"
        );

        await Assert.That(selected.ExitCode).IsEqualTo(0);
        await Assert.That(flattened.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "generated", "root.txt")))
            .IsTrue();
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "generated", "docs", "readme.md")))
            .IsTrue();
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "generated", "docs", "skip.md")))
            .IsFalse();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "flat", "guide.md"))).IsTrue();
    }

    [Test]
    public async Task LocalLink_WhenSelectionEmptyOrTargetsCollide_PreservesProjectState()
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory.FullName, "first"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory.FullName, "second"));
        File.WriteAllText(Path.Combine(sourceDirectory.FullName, "first", "shared.txt"), "first");
        File.WriteAllText(Path.Combine(sourceDirectory.FullName, "second", "shared.txt"), "second");
        await InitializeLocalSourceAsync(workspace.Path);
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        var lockPath = Path.Combine(workspace.Path, "lunapack-lock.yml");
        var initialConfiguration = File.ReadAllText(configurationPath);
        var initialLock = File.ReadAllText(lockPath);

        var empty = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "empty-selection",
            "--source",
            "local",
            "--include",
            "missing/**/*.md",
            "--install"
        );
        var collision = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "colliding-selection",
            "--source",
            "local",
            "--include",
            "**/*.txt",
            "--flatten",
            "--install"
        );

        await Assert.That(empty.ExitCode).IsEqualTo(1);
        await Assert.That(collision.ExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(initialLock);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "shared.txt"))).IsFalse();
    }

    [Test]
    public async Task LocalLink_WhenSourceAndTargetChange_ReportsAndAppliesLifecycleState()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var sourcePath = Path.Combine(workspace.Path, "source", "content.txt");
        var targetPath = Path.Combine(workspace.Path, "managed", "content.txt");
        File.WriteAllText(sourcePath, "version one");
        await InitializeLocalSourceAsync(workspace.Path);
        var installed = await AddInstalledLinkAsync(
            workspace.Path,
            "lifecycle-content",
            "content.txt",
            "managed"
        );

        var listed = await CliProcess.InvokeAsync(workspace.Path, "links", "list");
        var shown = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "show",
            "lifecycle-content"
        );
        File.WriteAllText(sourcePath, "version two");
        var outdated = await CliProcess.InvokeAsync(workspace.Path, "outdated");
        var updated = await CliProcess.InvokeAsync(workspace.Path, "update", "lifecycle-content");
        await Assert.That(updated.ExitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("version two");

        File.WriteAllText(targetPath, "local edit");
        var audit = await CliProcess.InvokeAsync(workspace.Path, "audit");
        var protectedUninstall = await CliProcess.InvokeAsync(
            workspace.Path,
            "uninstall",
            "lifecycle-content"
        );
        await Assert.That(protectedUninstall.ExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("local edit");

        File.WriteAllText(targetPath, "version two");
        var uninstalled = await CliProcess.InvokeAsync(
            workspace.Path,
            "uninstall",
            "lifecycle-content"
        );

        await AssertLifecycleInspectionAsync(installed, listed, shown, outdated);
        await Assert
            .That(audit.StandardOutput)
            .Contains("lifecycle-content")
            .And.Contains("modified");
        await Assert.That(uninstalled.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(targetPath)).IsFalse();
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml")))
            .Contains("lifecycle-content");
    }

    [Test]
    public async Task LocalLink_WhenForcedRemovalFindsModifiedTarget_PreservesOnlyModifiedContent()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        File.WriteAllText(Path.Combine(workspace.Path, "source", "first.txt"), "first");
        File.WriteAllText(Path.Combine(workspace.Path, "source", "second.txt"), "second");
        await InitializeLocalSourceAsync(workspace.Path);
        await AddInstalledLinkAsync(workspace.Path, "forced-content", "*.txt", "managed");
        var firstTarget = Path.Combine(workspace.Path, "managed", "first.txt");
        var secondTarget = Path.Combine(workspace.Path, "managed", "second.txt");
        File.WriteAllText(secondTarget, "local edit");

        var removed = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "rm",
            "forced-content",
            "--force"
        );

        await Assert.That(removed.ExitCode).IsEqualTo(0);
        await Assert.That(removed.StandardOutput).Contains("Preserved locally modified target");
        await Assert.That(File.Exists(firstTarget)).IsFalse();
        await Assert.That(File.ReadAllText(secondTarget)).IsEqualTo("local edit");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml")))
            .DoesNotContain("forced-content");
    }

    [Test]
    public async Task GitLink_WhenRefAdvances_UsesCacheAndDiffsSelectedContent()
    {
        using var workspace = new TestWorkspace();
        using var repository = new GitTestRepository();
        var repositoryPath = repository.Path;
        var (selectedPath, installedCommit) = await PrepareGitLinkRepositoryAsync(repositoryPath);
        var installed = await ConfigureAndInstallGitLinkAsync(workspace.Path, repositoryPath);
        var targetPath = Path.Combine(workspace.Path, ".github", "agents", "expert.agent.md");
        var cacheMetadata = FindCacheMetadata(installedCommit);
        var cachedMetadataContents = File.ReadAllText(cacheMetadata);
        var current = await CliProcess.InvokeAsync(workspace.Path, "outdated");

        await GitProcess.InvokeAsync(repositoryPath, "checkout", "--quiet", "release");
        File.WriteAllText(Path.Combine(repositoryPath, "unrelated.txt"), "unrelated");
        await CommitAllAsync(repositoryPath, "Unrelated content");
        var equivalentCommit = (
            await GitProcess.InvokeAsync(repositoryPath, "rev-parse", "HEAD")
        ).Trim();
        await GitProcess.InvokeAsync(repositoryPath, "checkout", "--quiet", "main");
        var equivalent = await CliProcess.InvokeAsync(workspace.Path, "outdated");
        var refreshed = await CliProcess.InvokeAsync(workspace.Path, "update", "git-agent");
        var refreshedLock = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));

        await GitProcess.InvokeAsync(repositoryPath, "checkout", "--quiet", "release");
        File.WriteAllText(selectedPath, "updated release content");
        await CommitAllAsync(repositoryPath, "Updated selected content");
        await GitProcess.InvokeAsync(repositoryPath, "checkout", "--quiet", "main");
        var changed = await CliProcess.InvokeAsync(workspace.Path, "outdated");
        var updated = await CliProcess.InvokeAsync(workspace.Path, "update", "git-agent");

        await Assert.That(installed.ExitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("updated release content");
        await Assert.That(cachedMetadataContents).Contains(installedCommit);
        await Assert.That(current.StandardOutput).DoesNotContain("git-agent");
        await Assert.That(equivalent.StandardOutput).DoesNotContain("git-agent");
        await Assert.That(refreshed.ExitCode).IsEqualTo(0);
        await Assert.That(refreshedLock).Contains(equivalentCommit);
        await Assert
            .That(changed.StandardOutput)
            .Contains("git-agent")
            .And.Contains("file contents changed");
        await Assert.That(updated.ExitCode).IsEqualTo(0);

        var sourceCacheDirectory = Directory
            .GetParent(Directory.GetParent(cacheMetadata)!.FullName)!
            .FullName;
        Directory.Delete(sourceCacheDirectory, recursive: true);
    }

    [Test]
    public async Task LocalLink_WhenPathEscapesOrSourceIdentityChanges_PreservesInstalledState()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "other"));
        File.WriteAllText(Path.Combine(workspace.Path, "source", "owned.txt"), "original");
        File.WriteAllText(Path.Combine(workspace.Path, "other", "owned.txt"), "redirected");
        await InitializeLocalSourceAsync(workspace.Path);
        await AddInstalledLinkAsync(workspace.Path, "protected-link", "owned.txt", "managed");
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        var lockPath = Path.Combine(workspace.Path, "lunapack-lock.yml");
        var targetPath = Path.Combine(workspace.Path, "managed", "owned.txt");
        var installedConfiguration = File.ReadAllText(configurationPath);
        var installedLock = File.ReadAllText(lockPath);

        var traversal = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "escaping-link",
            "--source",
            "local",
            "--include",
            "owned.txt",
            "--target",
            "../outside",
            "--install"
        );
        var redirectedConfiguration = installedConfiguration.Replace(
            "path: source",
            "path: other",
            StringComparison.Ordinal
        );
        File.WriteAllText(configurationPath, redirectedConfiguration);
        var redirected = await CliProcess.InvokeAsync(workspace.Path, "update", "protected-link");

        await Assert.That(traversal.ExitCode).IsEqualTo(1);
        await Assert.That(redirected.ExitCode).IsEqualTo(1);
        await Assert.That(redirected.StandardOutput).Contains("locked identity");
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("original");
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(installedLock);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(redirectedConfiguration);
    }

    [Test]
    public async Task LocalLink_WhenPackOwnsTarget_RejectsCrossRootOwnership()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        File.WriteAllText(Path.Combine(sourcePath.FullName, "raw.txt"), "link content");
        var packPath = Directory.CreateDirectory(Path.Combine(sourcePath.FullName, "owner"));
        Directory.CreateDirectory(Path.Combine(packPath.FullName, "templates"));
        File.WriteAllText(
            Path.Combine(packPath.FullName, "pack.yml"),
            "id: owner\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/owned.txt\n    target: raw.txt\n"
        );
        File.WriteAllText(
            Path.Combine(packPath.FullName, "templates", "owned.txt"),
            "pack content"
        );
        await InitializeLocalSourceAsync(workspace.Path);
        var packInstalled = await CliProcess.InvokeAsync(workspace.Path, "install", "owner");
        var conflict = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "conflicting-link",
            "--source",
            "local",
            "--include",
            "raw.txt",
            "--install"
        );

        await Assert.That(packInstalled.ExitCode).IsEqualTo(0);
        await Assert.That(conflict.ExitCode).IsEqualTo(1);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "raw.txt")))
            .IsEqualTo("pack content");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml")))
            .DoesNotContain("conflicting-link");
    }

    [Test]
    public async Task LocalLink_WhenStateSaveFails_RollsBackManagedFilesAndLockState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var sourcePath = Path.Combine(workspace.Path, "source", "content.txt");
        var targetPath = Path.Combine(workspace.Path, "managed", "content.txt");
        File.WriteAllText(sourcePath, "version one");
        await InitializeLocalSourceAsync(workspace.Path);
        await AddInstalledLinkAsync(workspace.Path, "rollback-link", "content.txt", "managed");
        var lockPath = Path.Combine(workspace.Path, "lunapack-lock.yml");
        var installedLock = File.ReadAllText(lockPath);
        File.WriteAllText(sourcePath, "version two");

        CliResult update;
        using (File.Open(lockPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            update = await CliProcess.InvokeAsync(workspace.Path, "update", "rollback-link");
        }

        await Assert.That(update.ExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("version one");
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(installedLock);
    }

    [Test]
    public async Task GitLink_WhenRefIsUnresolvedOrSelectionIsSymlink_PreservesProjectState()
    {
        using var workspace = new TestWorkspace();
        using var repository = new GitTestRepository();
        File.WriteAllText(Path.Combine(repository.Path, "target.txt"), "target");
        await InitializeGitRepositoryAsync(repository.Path, "Initial content");
        await AddGitSymlinkEntryAsync(repository.Path, "linked.txt", "target.txt");
        await CliProcess.InvokeAsync(workspace.Path, "init");
        await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            repository.Path
        );
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        var lockPath = Path.Combine(workspace.Path, "lunapack-lock.yml");
        var initialConfiguration = File.ReadAllText(configurationPath);
        var initialLock = File.ReadAllText(lockPath);

        var unresolved = await AddGitLinkAsync(
            workspace.Path,
            "unresolved-link",
            "target.txt",
            "missing"
        );
        var symlink = await AddGitLinkAsync(workspace.Path, "symlink-link", "linked.txt", "main");

        await Assert.That(unresolved.ExitCode).IsEqualTo(1);
        await Assert.That(symlink.ExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(initialLock);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "linked.txt"))).IsFalse();
    }

    [Test]
    public async Task LinkInspection_WhenPersistedDefinitionHasPackOnlyProperty_RejectsStateWithoutMutation()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        File.WriteAllText(Path.Combine(workspace.Path, "source", "content.txt"), "content");
        await InitializeLocalSourceAsync(workspace.Path);
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        const string invalidConfiguration =
            "links:\n  invalid-link:\n    source: local\n    includes:\n    - content.txt\n    parameters: {}\npacks: []\nschemaVersion: 1\nsources:\n- path: source\n  type: local\n  name: local\ntrust:\n  packs: []\n  sources: []\nvariables: {}\n";
        File.WriteAllText(configurationPath, invalidConfiguration);

        var result = await CliProcess.InvokeAsync(workspace.Path, "links", "list");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(invalidConfiguration);
    }

    [Test]
    public async Task LocalLink_WhenStateMutates_PreservesUnrelatedStateAndCanonicalizesPaths()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateRetainedStateSource(workspace.Path);
        await InitializeLocalSourceAsync(workspace.Path);
        await CliProcess.InvokeAsync(workspace.Path, "install", "retained-pack");
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        WriteRetainedConfiguration(configurationPath);
        var retainedAdded = await AddInstalledLinkAsync(
            workspace.Path,
            "retained-link",
            "retained.txt",
            "retained"
        );
        await Assert
            .That(retainedAdded.ExitCode)
            .IsEqualTo(0)
            .Because(retainedAdded.StandardOutput);
        var added = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "canonical-link",
            "--source",
            "local",
            "--path",
            "nested\\base",
            "--include",
            "docs\\guide.md",
            "--target",
            "generated\\links",
            "--install"
        );
        File.WriteAllText(
            Path.Combine(sourcePath.FullName, "nested", "base", "docs", "guide.md"),
            "version two"
        );
        var updated = await CliProcess.InvokeAsync(workspace.Path, "update", "canonical-link");
        var uninstalled = await CliProcess.InvokeAsync(
            workspace.Path,
            "uninstall",
            "canonical-link"
        );
        var configuration = File.ReadAllText(configurationPath);
        var lockFile = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));

        await Assert.That(added.ExitCode).IsEqualTo(0).Because(added.StandardOutput);
        await Assert.That(updated.ExitCode).IsEqualTo(0);
        await Assert.That(uninstalled.ExitCode).IsEqualTo(0);
        await Assert.That(configuration).Contains("retained-pack");
        await Assert.That(configuration).Contains("retained-link");
        await Assert.That(configuration).Contains("retainedMode: strict");
        await Assert.That(configuration).Contains("docs/old").And.Contains("docs/new");
        await Assert.That(configuration).Contains("docs/README.md");
        await Assert.That(configuration).Contains("canonical-link");
        await Assert.That(configuration).DoesNotContain("\\");
        await Assert.That(lockFile).Contains("retained-pack").And.Contains("retained-link");
        await Assert.That(lockFile).DoesNotContain("canonical-link");
        await Assert.That(lockFile).DoesNotContain("\\");
    }

    private static void CreateSelectionSource(string projectDirectory)
    {
        var sourceDirectory = Directory.CreateDirectory(
            Path.Combine(projectDirectory, "source", "content")
        );
        Directory.CreateDirectory(Path.Combine(sourceDirectory.FullName, "docs"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory.FullName, "prefix"));
        File.WriteAllText(Path.Combine(sourceDirectory.FullName, "root.txt"), "root");
        File.WriteAllText(Path.Combine(sourceDirectory.FullName, "docs", "readme.md"), "readme");
        File.WriteAllText(Path.Combine(sourceDirectory.FullName, "docs", "skip.md"), "skip");
        File.WriteAllText(Path.Combine(sourceDirectory.FullName, "prefix", "guide.md"), "guide");
    }

    private static async Task AssertLifecycleInspectionAsync(
        CliResult installed,
        CliResult listed,
        CliResult shown,
        CliResult outdated
    )
    {
        await Assert.That(installed.ExitCode).IsEqualTo(0);
        await Assert
            .That(listed.StandardOutput)
            .Contains("lifecycle-content")
            .And.Contains("installed");
        await Assert
            .That(shown.StandardOutput)
            .Contains("lifecycle-content")
            .And.Contains("Selected files");
        await Assert
            .That(outdated.StandardOutput)
            .Contains("lifecycle-content")
            .And.Contains("file contents changed");
    }

    private static async Task<(string SelectedPath, string Commit)> PrepareGitLinkRepositoryAsync(
        string repositoryPath
    )
    {
        Directory.CreateDirectory(Path.Combine(repositoryPath, "agents"));
        var selectedPath = Path.Combine(repositoryPath, "agents", "expert.agent.md");
        File.WriteAllText(selectedPath, "main content");
        await InitializeGitRepositoryAsync(repositoryPath, "Initial main content");
        await GitProcess.InvokeAsync(repositoryPath, "checkout", "--quiet", "-b", "release");
        File.WriteAllText(selectedPath, "release content");
        await CommitAllAsync(repositoryPath, "Release content");
        var commit = (await GitProcess.InvokeAsync(repositoryPath, "rev-parse", "HEAD")).Trim();
        await GitProcess.InvokeAsync(repositoryPath, "checkout", "--quiet", "main");
        return (selectedPath, commit);
    }

    private static async Task<CliResult> ConfigureAndInstallGitLinkAsync(
        string projectDirectory,
        string repositoryPath
    )
    {
        await CliProcess.InvokeAsync(projectDirectory, "init");
        await CliProcess.InvokeAsync(
            projectDirectory,
            "sources",
            "add",
            "git",
            "git",
            repositoryPath,
            "--ref",
            "main"
        );
        return await CliProcess.InvokeAsync(
            projectDirectory,
            "links",
            "add",
            "git-agent",
            "--source",
            "git",
            "--path",
            "agents",
            "--include",
            "expert.agent.md",
            "--target",
            ".github/agents",
            "--ref",
            "release",
            "--install"
        );
    }

    private static async Task AddGitSymlinkEntryAsync(
        string repositoryPath,
        string linkPath,
        string target
    )
    {
        var pointerPath = Path.Combine(repositoryPath, ".git", "symlink-target");
        File.WriteAllText(pointerPath, target);
        var blob = (
            await GitProcess.InvokeAsync(repositoryPath, "hash-object", "-w", pointerPath)
        ).Trim();
        await GitProcess.InvokeAsync(
            repositoryPath,
            "update-index",
            "--add",
            "--cacheinfo",
            $"120000,{blob},{linkPath}"
        );
        await GitProcess.InvokeAsync(
            repositoryPath,
            "-c",
            "user.email=lunapack@example.test",
            "-c",
            "user.name=Lunapack Test",
            "commit",
            "--quiet",
            "-m",
            "Add symbolic link"
        );
    }

    private static async Task<CliResult> AddGitLinkAsync(
        string projectDirectory,
        string name,
        string include,
        string reference
    ) =>
        await CliProcess.InvokeAsync(
            projectDirectory,
            "links",
            "add",
            name,
            "--source",
            "git",
            "--include",
            include,
            "--ref",
            reference,
            "--install"
        );

    private static DirectoryInfo CreateRetainedStateSource(string projectDirectory)
    {
        var sourcePath = Directory.CreateDirectory(Path.Combine(projectDirectory, "source"));
        Directory.CreateDirectory(Path.Combine(sourcePath.FullName, "nested", "base", "docs"));
        File.WriteAllText(
            Path.Combine(sourcePath.FullName, "nested", "base", "docs", "guide.md"),
            "version one"
        );
        File.WriteAllText(Path.Combine(sourcePath.FullName, "retained.txt"), "retained link");
        var packPath = Directory.CreateDirectory(
            Path.Combine(sourcePath.FullName, "retained-pack")
        );
        Directory.CreateDirectory(Path.Combine(packPath.FullName, "templates"));
        File.WriteAllText(
            Path.Combine(packPath.FullName, "pack.yml"),
            "id: retained-pack\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: pack-owned.txt\n"
        );
        File.WriteAllText(Path.Combine(packPath.FullName, "templates", "content.txt"), "pack");
        return sourcePath;
    }

    private static void WriteRetainedConfiguration(string configurationPath) =>
        File.WriteAllText(
            configurationPath,
            "links: {}\npacks:\n- id: retained-pack\n  version: 1.0.0\nremap:\n  directories:\n    'docs\\old': 'docs\\new'\n  files:\n    'README.md': 'docs\\README.md'\nschemaVersion: 1\nsources:\n- path: source\n  type: local\n  name: local\ntrust:\n  packs:\n  - id: retained-pack\n    source: local\n  sources:\n  - local\nvariables:\n  retainedMode: strict\n"
        );

    private static string FindCacheMetadata(string commit)
    {
        var cacheRoot =
            OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LunaPack",
                    "cache",
                    "sources"
                )
            : OperatingSystem.IsMacOS()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Caches",
                    "LunaPack",
                    "sources"
                )
            : Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".cache"
                    ),
                "lunapack",
                "sources"
            );

        return Directory
            .EnumerateFiles(cacheRoot, "metadata.json", SearchOption.AllDirectories)
            .Single(path => path.Contains(commit, StringComparison.Ordinal));
    }

    private static async Task InitializeGitRepositoryAsync(string repositoryPath, string message)
    {
        await GitProcess.InvokeAsync(repositoryPath, "init", "--initial-branch=main");
        await CommitAllAsync(repositoryPath, message);
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

    private static async Task<CliResult> AddInstalledLinkAsync(
        string projectDirectory,
        string name,
        string include,
        string target
    ) =>
        await CliProcess.InvokeAsync(
            projectDirectory,
            "links",
            "add",
            name,
            "--source",
            "local",
            "--include",
            include,
            "--target",
            target,
            "--install"
        );

    private static async Task InitializeLocalSourceAsync(string projectDirectory)
    {
        var initialized = await CliProcess.InvokeAsync(projectDirectory, "init");
        var sourced = await CliProcess.InvokeAsync(
            projectDirectory,
            "sources",
            "add",
            "local",
            "local",
            "source"
        );

        await Assert.That(initialized.ExitCode).IsEqualTo(0);
        await Assert.That(sourced.ExitCode).IsEqualTo(0);
    }
}
