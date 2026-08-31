using System.Text;

namespace Lunapack.Cli.IntegrationTests.Scenarios.Core;

[Property("FileSystem", "Real")]
public sealed class CliProcessTests
{
    [Test]
    public async Task CoreWorkflow_WhenCommandsSucceed_GuidesEachNextTransition()
    {
        using var workspace = new TestWorkspace();

        var root = await CliProcess.InvokeAsync(workspace.Path);
        var initialized = await CliProcess.InvokeAsync(workspace.Path, "init");
        var sourceDirectory = CreatePackSource(
            workspace.Path,
            ("example-v1", "example", "1.0.0", null, "version one")
        );
        var sourced = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "local",
            "local",
            sourceDirectory
        );
        var discovered = await CliProcess.InvokeAsync(workspace.Path, "discover");
        var installed = await CliProcess.InvokeAsync(workspace.Path, "install", "example");
        CreatePackSource(workspace.Path, ("example-v2", "example", "2.0.0", null, "version two"));
        var updated = await CliProcess.InvokeAsync(workspace.Path, "update");

        await Assert.That(root.StandardOutput).Contains("luna init");
        await Assert.That(initialized.StandardOutput).Contains("luna sources add git");
        await Assert.That(sourced.StandardOutput).Contains("luna discover");
        await Assert.That(discovered.StandardOutput).Contains("luna install <pack>");
        await Assert.That(installed.StandardOutput).Contains("luna outdated");
        await Assert.That(updated.StandardOutput).Contains("luna audit");
    }

    [Test]
    public async Task Cli_WhenHelpRequested_ReturnsCommandHelp()
    {
        using var workspace = new TestWorkspace();

        var result = await CliProcess.InvokeAsync(workspace.Path, "--help");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains("Usage:");
    }

    [Test]
    public async Task Cli_WhenLogLevelDebug_WritesPrefixedOutputAndWarningSuppressesIt()
    {
        using var workspace = new TestWorkspace();

        var debugLogging = await CliProcess.InvokeAsync(
            workspace.Path,
            "--log-level",
            "debug",
            "--help"
        );
        var warningLogging = await CliProcess.InvokeAsync(
            workspace.Path,
            "--log-level",
            "warning",
            "--help"
        );

        await Assert.That(debugLogging.StandardOutput).Contains("debug: Running CLI command");
        await Assert.That(debugLogging.StandardError).IsEmpty();
        await Assert
            .That(warningLogging.StandardOutput)
            .DoesNotContain("debug: Running CLI command");
        await Assert.That(warningLogging.StandardError).IsEmpty();
    }

    [Test]
    public async Task SourcesList_WhenSourcesConfigured_OutputsTypesAndProperties()
    {
        using var workspace = new TestWorkspace();
        using var repository = await CreateGitPackSourceAsync();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "packs"));
        await CliProcess.InvokeAsync(workspace.Path, "init");
        await CliProcess.InvokeAsync(workspace.Path, "sources", "add", "local", "local", "packs");
        var gitSource = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            repository.Path,
            "--ref",
            "main",
            "--path",
            "packs"
        );

        var result = await CliProcess.InvokeAsync(workspace.Path, "sources", "list");
        var output = result.StandardOutput.ReplaceLineEndings(string.Empty);

        await Assert.That(gitSource.ExitCode).IsEqualTo(0);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert
            .That(output)
            .Contains("local - local - path: packs - identity: local(path=packs)");
        await Assert
            .That(output)
            .Contains($"git - git - url: {repository.Path} - ref: refs/heads/main - path: packs");
        await Assert
            .That(output)
            .Contains($"identity: git(url={repository.Path}, ref=refs/heads/main, path=packs)");
        await Assert
            .That(output.IndexOf("local - local", StringComparison.Ordinal))
            .IsLessThan(output.IndexOf("git - git", StringComparison.Ordinal));
    }

    [Test]
    public async Task PackLifecycle_WhenManagedContentUnchanged_InstallsAndUninstalls()
    {
        using var workspace = new TestWorkspace();

        await InitializeAndAddSampleSourceAsync(workspace.Path);
        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "dotnet-gitignore");
        var managedFilePath = Path.Combine(workspace.Path, ".gitignore");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(managedFilePath)).IsTrue();

        var uninstall = await CliProcess.InvokeAsync(
            workspace.Path,
            "uninstall",
            "dotnet-gitignore"
        );

        await Assert.That(uninstall.ExitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(managedFilePath)).IsEmpty();
    }

    [Test]
    public async Task Install_WhenPackAlreadyInstalled_PreservesManagedFileAndProjectState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            ("one", "example", "1.0.0", null, "managed content")
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        var firstInstall = await CliProcess.InvokeAsync(workspace.Path, "install", "example");
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        var lockPath = Path.Combine(workspace.Path, "lunapack-lock.yml");
        var managedPath = Path.Combine(workspace.Path, ".pack");
        var installedConfiguration = File.ReadAllText(configurationPath);
        var installedLock = File.ReadAllText(lockPath);
        var installedContent = File.ReadAllText(managedPath);

        var repeatedInstall = await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        await Assert.That(firstInstall.ExitCode).IsEqualTo(0);
        await Assert.That(repeatedInstall.ExitCode).IsEqualTo(1);
        await Assert.That(repeatedInstall.StandardOutput).Contains("already installed");
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(installedConfiguration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(installedLock);
        await Assert.That(File.ReadAllText(managedPath)).IsEqualTo(installedContent);
    }

    [Test]
    [Arguments("local-user")]
    [Arguments("project")]
    [Arguments("global-user")]
    public async Task PackLifecycle_WhenScriptsDenied_CannotBypassAndReportsScope(string scopeName)
    {
        using var workspace = new TestWorkspace();
        var profilePath = Path.Combine(workspace.Path, "profile");
        Directory.CreateDirectory(profilePath);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["LUNAPACK_USER_PROFILE"] = profilePath,
        };
        await CliProcess.InvokeAsync(workspace.Path, environment, "init");
        var sourcePath = CreateInstructionPackSource(
            workspace.Path,
            "example",
            "id: example\nversion: 1.0.0\nhooks:\n  preInstall:\n    - type: script\n      command: dotnet\n      arguments:\n        - --version\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
        );
        await CliProcess.InvokeAsync(
            workspace.Path,
            environment,
            "sources",
            "add",
            "local",
            "local",
            sourcePath
        );
        var denyArguments = scopeName switch
        {
            "project" => new[] { "trust", "scripts", "deny", "--project" },
            "global-user" => new[] { "trust", "scripts", "deny", "--global" },
            _ => new[] { "trust", "scripts", "deny" },
        };
        var denied = await CliProcess.InvokeAsync(workspace.Path, environment, denyArguments);
        var listArguments = scopeName switch
        {
            "project" => new[] { "trust", "list", "--project" },
            "global-user" => new[] { "trust", "list", "--global" },
            _ => new[] { "trust", "list" },
        };
        var listed = await CliProcess.InvokeAsync(workspace.Path, environment, listArguments);
        var dryRun = await CliProcess.InvokeAsync(
            workspace.Path,
            environment,
            "install",
            "example",
            "--dry-run",
            "--scripts",
            "run"
        );
        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            environment,
            "install",
            "example",
            "--scripts",
            "run"
        );
        var dryRunOutput = dryRun.StandardOutput.ReplaceLineEndings(" ");
        var installOutput = install.StandardOutput.ReplaceLineEndings(" ");

        await Assert.That(denied.ExitCode).IsEqualTo(0);
        await Assert.That(listed.StandardOutput).Contains($"{scopeName} script denial");
        await Assert.That(dryRunOutput).Contains($"blocked (policy: {scopeName})");
        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(installOutput).Contains("Lifecycle script denied by policy:");
        await Assert.That(installOutput).Contains(scopeName);
        await Assert.That(installOutput).DoesNotContain("10.0.");
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".pack"))).IsTrue();
    }

    [Test]
    public async Task PackLifecycle_WhenInstructionsMixed_RendersOrderedNonInteractiveContent()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateInstructionPackSource(
            workspace.Path,
            "example",
            "id: example\nversion: 1.0.0\nparameters:\n  companyName:\n    type: string\n    required: true\nhooks:\n  preInstall:\n    - type: instruction\n      file: instructions/first.md\n    - type: script\n      command: dotnet\n      arguments:\n        - --version\n    - type: instruction\n      file: instructions/last.md\n      templating: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["first.md"] = "## First\nstatic-instruction",
                ["last.md"] = "## Last\nHello {{ companyName }}",
            }
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "example",
            "--parameter",
            "companyName=Example Corp",
            "--scripts",
            "run"
        );

        await Assert.That(install.ExitCode).IsEqualTo(0);
        var first = install.StandardOutput.IndexOf("static-instruction", StringComparison.Ordinal);
        var script = install.StandardOutput.IndexOf("10.0.", StringComparison.Ordinal);
        var last = install.StandardOutput.IndexOf("Hello Example Corp", StringComparison.Ordinal);
        var success = install.StandardOutput.IndexOf(
            "Installed 'example' (version '1.0.0') in ",
            StringComparison.Ordinal
        );
        await Assert.That(first >= 0 && first < script && script < last && last < success).IsTrue();
        await Assert.That(install.StandardOutput).DoesNotContain("Press Enter to continue...");
        await Assert.That(install.StandardOutput).DoesNotContain("Applied managed-file changes");
    }

    [Test]
    public async Task PackLifecycle_WhenUninstallInstructionsDeclared_RendersAroundRemoval()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateInstructionPackSource(
            workspace.Path,
            "example",
            "id: example\nversion: 1.0.0\nhooks:\n  preUninstall:\n    - type: instruction\n      file: instructions/before.md\n  postUninstall:\n    - type: instruction\n      file: instructions/after.md\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["before.md"] = "## Before\nbefore-removal",
                ["after.md"] = "## After\nafter-removal",
            }
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        var uninstall = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "example");

        await Assert.That(uninstall.ExitCode).IsEqualTo(0);
        var before = uninstall.StandardOutput.IndexOf("before-removal", StringComparison.Ordinal);
        var after = uninstall.StandardOutput.IndexOf("after-removal", StringComparison.Ordinal);
        var success = uninstall.StandardOutput.IndexOf(
            "Uninstalled 'example' in ",
            StringComparison.Ordinal
        );
        await Assert.That(before >= 0 && before < after && after < success).IsTrue();
        await Assert.That(uninstall.StandardOutput).DoesNotContain("Applied managed-file changes");
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".pack"))).IsFalse();
    }

    [Test]
    public async Task PackLifecycle_WhenPostUninstallCommandFails_RestoresFilesAndState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateInstructionPackSource(
            workspace.Path,
            "example",
            "id: example\nversion: 1.0.0\nhooks:\n  postUninstall:\n    - type: script\n      command: dotnet\n      arguments:\n        - definitely-not-a-command\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        var uninstall = await CliProcess.InvokeAsync(
            workspace.Path,
            "uninstall",
            "example",
            "--scripts",
            "run"
        );

        await Assert.That(uninstall.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".pack"))).IsTrue();
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml")))
            .Contains("id: example");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml")))
            .Contains("id: example");
    }

    [Test]
    public async Task PackLifecycle_WhenSourceUnavailable_UninstallsWithoutHooks()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateInstructionPackSource(
            workspace.Path,
            "example",
            "id: example\nversion: 1.0.0\nhooks:\n  preUninstall:\n    - type: instruction\n      file: instructions/before.md\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["before.md"] = "## Before\nshould-not-render",
            }
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        await CliProcess.InvokeAsync(workspace.Path, "install", "example");
        Directory.Delete(Path.Combine(workspace.Path, sourcePath), recursive: true);

        var uninstall = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "example");

        await Assert.That(uninstall.ExitCode).IsEqualTo(0);
        await Assert
            .That(uninstall.StandardOutput)
            .Contains("Uninstall hooks for pack 'example' are unavailable");
        await Assert.That(uninstall.StandardOutput).DoesNotContain("should-not-render");
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".pack"))).IsFalse();
    }

    [Test]
    public async Task PackLifecycle_WhenUninstallHasNoHooks_DoesNotResolveRequiredParameters()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateInstructionPackSource(
            workspace.Path,
            "example",
            "id: example\nversion: 1.0.0\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "example",
            "--parameter",
            "companyName=Example Corp"
        );

        var uninstall = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "example");

        await Assert.That(uninstall.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".pack"))).IsFalse();
    }

    [Test]
    public async Task PackLifecycle_WhenNewerReleaseExists_UninstallUsesInstalledReleaseHooks()
    {
        using var workspace = new TestWorkspace();
        var sourceRoot = Path.Combine(workspace.Path, "source");
        CreateInstructionPack(
            sourceRoot,
            "example-v1",
            "id: example\nversion: 1.0.0\nhooks:\n  preUninstall:\n    - type: instruction\n      file: instructions/remove.md\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["remove.md"] = "## Remove\ninstalled-release-hook",
            }
        );
        CreateInstructionPack(
            sourceRoot,
            "example-v2",
            "id: example\nversion: 2.0.0\nhooks:\n  preUninstall:\n    - type: instruction\n      file: instructions/remove.md\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["remove.md"] = "## Remove\nnewer-release-hook",
            }
        );
        await InitializeAndAddSourceAsync(workspace.Path, "source");
        await CliProcess.InvokeAsync(workspace.Path, "install", "example@1.0.0");

        var uninstall = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "example");

        await Assert.That(uninstall.ExitCode).IsEqualTo(0);
        await Assert.That(uninstall.StandardOutput).Contains("installed-release-hook");
        await Assert.That(uninstall.StandardOutput).DoesNotContain("newer-release-hook");
    }

    [Test]
    [Arguments("missing")]
    [Arguments("invalid")]
    public async Task PackLifecycle_WhenInstructionPreparationFails_PreservesProjectState(
        string failure
    )
    {
        using var workspace = new TestWorkspace();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.Equals(failure, "invalid", StringComparison.Ordinal))
        {
            files["setup.md"] = "{{ 1 + }}";
        }

        var sourcePath = CreateInstructionPackSource(
            workspace.Path,
            "example",
            "id: example\nversion: 1.0.0\nhooks:\n  preInstall:\n    - type: instruction\n      file: instructions/setup.md\n      templating: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            files
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        var lockPath = Path.Combine(workspace.Path, "lunapack-lock.yml");
        var initialConfiguration = File.ReadAllText(configurationPath);
        var initialLock = File.ReadAllText(lockPath);

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        await Assert.That(install.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".pack"))).IsFalse();
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(initialLock);
    }

    [Test]
    public async Task PackLifecycle_WhenUpdateInstructionTemplated_RendersPreparedContent()
    {
        using var workspace = new TestWorkspace();
        var sourceRoot = Path.Combine(workspace.Path, "source");
        CreateInstructionPack(
            sourceRoot,
            "example-v1",
            "id: example\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal),
            "one"
        );
        CreateInstructionPack(
            sourceRoot,
            "example-v2",
            "id: example\nversion: 2.0.0\nhooks:\n  postUpdate:\n    - type: instruction\n      file: instructions/update.md\n      templating: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["update.md"] = "## Updated\nYear {{ date.now.year }}",
            },
            "two"
        );
        await InitializeAndAddSourceAsync(workspace.Path, "source");
        await CliProcess.InvokeAsync(workspace.Path, "install", "example@1.0.0");

        var update = await CliProcess.InvokeAsync(workspace.Path, "update", "example");

        await Assert.That(update.ExitCode).IsEqualTo(0);
        await Assert.That(update.StandardOutput).Contains($"Year {DateTime.Now.Year}");
        await Assert.That(File.ReadAllText(Path.Combine(workspace.Path, ".pack"))).IsEqualTo("two");
    }

    [Test]
    public async Task PackLifecycle_WhenTransientEventSuppressed_DoesNotLoadInstruction()
    {
        using var workspace = new TestWorkspace();
        var sourceRoot = Path.Combine(workspace.Path, "source");
        CreateInstructionPack(
            sourceRoot,
            "dependency",
            "id: dependency\nversion: 1.0.0\nhooks:\n  preInstall:\n    - type: instruction\n      file: instructions/missing.md\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
        );
        CreateInstructionPack(
            sourceRoot,
            "root",
            "id: root\nversion: 1.0.0\npacks:\n  - id: dependency\n    version: 1.0.0\n    disabledHooks:\n      - preInstall\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
        );
        await InitializeAndAddSourceAsync(workspace.Path, "source");

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "root");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".pack"))).IsTrue();
    }

    [Test]
    public async Task PackLifecycle_WhenGitSourceUsesDefaultBranch_InstallsAndLocksProvenance()
    {
        using var workspace = new TestWorkspace();
        using var repository = await CreateGitPackSourceAsync();
        var init = await CliProcess.InvokeAsync(workspace.Path, "init");
        var source = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            repository.Path,
            "--path",
            "packs"
        );

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example");
        if (install.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git source installation failed: {install.StandardOutput}{install.StandardError}"
            );
        }

        await Assert.That(init.ExitCode).IsEqualTo(0);
        await Assert.That(source.ExitCode).IsEqualTo(0);
        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".pack")))
            .IsEqualTo("from git");
        var lockFile = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));
        await Assert.That(lockFile).Contains("gitSource:");
        await Assert.That(lockFile).Contains($"url: {repository.Path}");
        await Assert.That(lockFile).Contains("resolvedCommit:");
    }

    [Test]
    public async Task PackLifecycle_WhenGitSourceUsesExplicitCommit_InstallsPinnedContent()
    {
        using var workspace = new TestWorkspace();
        using var repository = await CreateGitPackSourceAsync();
        var initialCommit = (
            await GitProcess.InvokeAsync(repository.Path, "rev-parse", "HEAD")
        ).Trim();
        File.WriteAllText(
            Path.Combine(repository.Path, "packs", "example", "templates", "content.txt"),
            "from newer commit"
        );
        await GitProcess.InvokeAsync(repository.Path, "add", ".");
        await GitProcess.InvokeAsync(
            repository.Path,
            "-c",
            "user.email=lunapack@example.test",
            "-c",
            "user.name=Lunapack Test",
            "commit",
            "--quiet",
            "-m",
            "Advance pack source"
        );
        await CliProcess.InvokeAsync(workspace.Path, "init");
        var source = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            repository.Path,
            "--ref",
            initialCommit,
            "--path",
            "packs"
        );

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        await Assert.That(source.ExitCode).IsEqualTo(0);
        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".pack")))
            .IsEqualTo("from git");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml")))
            .Contains($"ref: {initialCommit}");
    }

    [Test]
    public async Task PackLifecycle_WhenGitSourceUsesExplicitBranch_InstallsBranchContent()
    {
        using var workspace = new TestWorkspace();
        using var repository = await CreateGitPackSourceAsync();
        await GitProcess.InvokeAsync(repository.Path, "checkout", "--quiet", "-b", "release");
        File.WriteAllText(
            Path.Combine(repository.Path, "packs", "example", "templates", "content.txt"),
            "from release branch"
        );
        await GitProcess.InvokeAsync(repository.Path, "add", ".");
        await GitProcess.InvokeAsync(
            repository.Path,
            "-c",
            "user.email=lunapack@example.test",
            "-c",
            "user.name=Lunapack Test",
            "commit",
            "--quiet",
            "-m",
            "Release pack source"
        );
        await GitProcess.InvokeAsync(repository.Path, "checkout", "--quiet", "main");
        await CliProcess.InvokeAsync(workspace.Path, "init");
        var source = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            repository.Path,
            "--ref",
            "release",
            "--path",
            "packs"
        );

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        await Assert.That(source.ExitCode).IsEqualTo(0);
        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".pack")))
            .IsEqualTo("from release branch");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml")))
            .Contains("ref: refs/heads/release");
    }

    [Test]
    public async Task Discover_WhenGitSourceContainsInvalidManifest_ExcludesInvalidCandidate()
    {
        using var workspace = new TestWorkspace();
        using var repository = await CreateGitPackSourceAsync();
        var invalidPackDirectory = Directory.CreateDirectory(
            Path.Combine(repository.Path, "packs", "invalid")
        );
        File.WriteAllText(
            Path.Combine(invalidPackDirectory.FullName, "pack.yml"),
            "id: invalid\nversion: not-a-version\n"
        );
        await GitProcess.InvokeAsync(repository.Path, "add", ".");
        await GitProcess.InvokeAsync(
            repository.Path,
            "-c",
            "user.email=lunapack@example.test",
            "-c",
            "user.name=Lunapack Test",
            "commit",
            "--quiet",
            "-m",
            "Add invalid candidate"
        );
        await CliProcess.InvokeAsync(workspace.Path, "init");
        await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            repository.Path,
            "--path",
            "packs"
        );

        var discover = await CliProcess.InvokeAsync(workspace.Path, "discover");

        await Assert.That(discover.ExitCode).IsEqualTo(0);
        await Assert.That(discover.StandardOutput).Contains("example");
        await Assert.That(discover.StandardOutput).Contains("1.0.0");
        await Assert.That(discover.StandardOutput).DoesNotContain("invalid");
        await Assert.That(discover.StandardOutput).DoesNotContain("excluded");
    }

    [Test]
    public async Task Discover_WhenGitDefaultBranchAdvances_RefreshesCachedCatalog()
    {
        using var workspace = new TestWorkspace();
        using var repository = await CreateGitPackSourceAsync();
        await CliProcess.InvokeAsync(workspace.Path, "init");
        await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            repository.Path,
            "--path",
            "packs"
        );
        var initialDiscover = await CliProcess.InvokeAsync(workspace.Path, "discover");
        var cacheFilePath = Directory
            .GetFiles(Path.Combine(workspace.Path, ".lunapack", "git-sources"))
            .Single();
        var expectedCacheTimestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(cacheFilePath, expectedCacheTimestamp);

        var cachedDiscover = await CliProcess.InvokeAsync(workspace.Path, "discover");
        var cacheHitTimestamp = File.GetLastWriteTimeUtc(cacheFilePath);

        var updatedPackDirectory = Directory.CreateDirectory(
            Path.Combine(repository.Path, "packs", "example-2", "templates")
        );
        File.WriteAllText(
            Path.Combine(updatedPackDirectory.Parent.RequireNotNull().FullName, "pack.yml"),
            "id: example\nversion: 2.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n"
        );
        File.WriteAllText(
            Path.Combine(updatedPackDirectory.FullName, "content.txt"),
            "from updated cache"
        );
        await GitProcess.InvokeAsync(repository.Path, "add", ".");
        await GitProcess.InvokeAsync(
            repository.Path,
            "-c",
            "user.email=lunapack@example.test",
            "-c",
            "user.name=Lunapack Test",
            "commit",
            "--quiet",
            "-m",
            "Add newer pack version"
        );

        var refreshedDiscover = await CliProcess.InvokeAsync(workspace.Path, "discover");

        await Assert.That(initialDiscover.ExitCode).IsEqualTo(0);
        await Assert.That(cachedDiscover.ExitCode).IsEqualTo(0);
        await Assert.That(cacheHitTimestamp).IsEqualTo(expectedCacheTimestamp);
        await Assert
            .That(File.GetLastWriteTimeUtc(cacheFilePath))
            .IsGreaterThan(expectedCacheTimestamp);
        await Assert.That(refreshedDiscover.ExitCode).IsEqualTo(0);
        await Assert.That(refreshedDiscover.StandardOutput).Contains("example");
        await Assert.That(refreshedDiscover.StandardOutput).Contains("2.0.0");
    }

    [Test]
    public async Task Discover_WhenGitSourceIsUnavailable_ReportsFailureAndPreservesState()
    {
        using var workspace = new TestWorkspace();
        var unavailableRepositoryPath = Path.Combine(
            Path.GetTempPath(),
            $"lunapack-missing-git-source-{Guid.NewGuid():N}"
        );
        await CliProcess.InvokeAsync(workspace.Path, "init");
        await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            unavailableRepositoryPath
        );
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        var lockFilePath = Path.Combine(workspace.Path, "lunapack-lock.yml");
        var initialConfiguration = File.ReadAllText(configurationPath);
        var initialLockFile = File.ReadAllText(lockFilePath);

        var discover = await CliProcess.InvokeAsync(workspace.Path, "discover");

        await Assert.That(discover.ExitCode).IsEqualTo(1);
        await Assert.That(discover.StandardOutput).Contains("error: Git exited with code");
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
        await Assert.That(File.ReadAllText(lockFilePath)).IsEqualTo(initialLockFile);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".pack"))).IsFalse();
    }

    [Test]
    public async Task PackLifecycle_WhenGitCompositeSelected_InstallsDependenciesAndLocksProvenance()
    {
        using var workspace = new TestWorkspace();
        using var repository = await CreateGitPackSourceAsync(
            (
                "shared",
                "id: shared\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: shared.txt\n",
                "shared from git"
            ),
            (
                "foundation",
                "id: foundation\nversion: 1.0.0\npacks:\n  - id: shared\n    version: 1.0.0\n",
                null
            )
        );
        await CliProcess.InvokeAsync(workspace.Path, "init");
        await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            repository.Path,
            "--path",
            "packs"
        );

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "foundation");

        var lockFile = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));
        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "shared.txt")))
            .IsEqualTo("shared from git");
        await Assert.That(lockFile).Contains("id: foundation");
        await Assert.That(lockFile).Contains("id: shared");
        await Assert
            .That(lockFile.Split("gitSource:", StringSplitOptions.None).Length)
            .IsEqualTo(3);
        await Assert.That(lockFile).Contains($"url: {repository.Path}");
        await Assert.That(lockFile).Contains("resolvedCommit:");
    }

    [Test]
    public async Task PackLifecycle_WhenLocalAndGitPacksTie_UsesEarlierConfiguredSource()
    {
        using var workspace = new TestWorkspace();
        using var repository = await CreateGitPackSourceAsync();
        var localSourcePath = CreatePackSource(
            workspace.Path,
            ("example", "example", "1.0.0", null, "from local")
        );
        await CliProcess.InvokeAsync(workspace.Path, "init");
        await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "local",
            "local",
            localSourcePath
        );
        await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            repository.Path,
            "--path",
            "packs"
        );

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".pack")))
            .IsEqualTo("from local");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml")))
            .DoesNotContain("gitSource:");
    }

    [Test]
    public async Task PackLifecycle_WhenGitSourceAdvances_UpdatesInstalledRelease()
    {
        using var workspace = new TestWorkspace();
        using var repository = await CreateGitPackSourceAsync();
        await CliProcess.InvokeAsync(workspace.Path, "init");
        await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "git",
            repository.Path,
            "--path",
            "packs"
        );
        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example@1.0.0");
        CreateGitPack(
            repository.Path,
            (
                "example-1-1",
                "id: example\nversion: 1.1.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n",
                "from updated git"
            )
        );
        await GitProcess.InvokeAsync(repository.Path, "add", ".");
        await GitProcess.InvokeAsync(
            repository.Path,
            "-c",
            "user.email=lunapack@example.test",
            "-c",
            "user.name=Lunapack Test",
            "commit",
            "--quiet",
            "-m",
            "Add Git update"
        );

        var update = await CliProcess.InvokeAsync(workspace.Path, "update", "example");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(update.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".pack")))
            .IsEqualTo("from updated git");
        var lockFile = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));
        await Assert.That(lockFile).Contains("version: 1.1.0");
        await Assert.That(lockFile).Contains("gitSource:");
        await Assert.That(lockFile).Contains("resolvedCommit:");
    }

    [Test]
    public async Task PackLifecycle_WhenDestinationSelected_InstallsAndUninstallsEffectiveTarget()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path, ("one", "example", "1.0.0", null, "one"));
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        var managedFilePath = Path.Combine(workspace.Path, "docs", "guidance", ".pack");

        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "example",
            "--destination",
            "docs/guidance"
        );

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(managedFilePath)).IsTrue();

        var uninstall = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "example");

        await Assert.That(uninstall.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(managedFilePath)).IsFalse();
    }

    [Test]
    public async Task PackLifecycle_WhenManagedTargetRemapped_RetainsAndRelocatesLockTarget()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateRemappableVersionedPackSource(workspace.Path);
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        var remap = await CliProcess.InvokeAsync(
            workspace.Path,
            "remap",
            "set",
            "directory",
            "docs/adr",
            "docs/architecture/adr"
        );
        await Assert.That(remap.ExitCode).IsEqualTo(0);

        var inspect = await CliProcess.InvokeAsync(workspace.Path, "inspect", "example");
        await Assert.That(inspect.ExitCode).IsEqualTo(0);
        await Assert
            .That(inspect.StandardOutput)
            .Contains("docs/adr/template.md -> docs/architecture/adr/template.md");

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example@1.0.0");
        var initialTarget = Path.Combine(
            workspace.Path,
            "docs",
            "architecture",
            "adr",
            "template.md"
        );
        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(initialTarget)).IsEqualTo("version one");

        var update = await CliProcess.InvokeAsync(workspace.Path, "update", "example");
        await Assert.That(update.ExitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(initialTarget)).IsEqualTo("version two");

        var movedTarget = Path.Combine(workspace.Path, "docs", "managed", "template.md");
        var move = await CliProcess.InvokeAsync(
            workspace.Path,
            "mv",
            "docs/architecture/adr/template.md",
            "docs/managed/template.md"
        );
        await Assert.That(move.ExitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(movedTarget)).IsEqualTo("version two");

        var reboundTarget = Path.Combine(workspace.Path, "docs", "rebinding", "template.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reboundTarget).RequireNotNull());
        File.Move(movedTarget, reboundTarget);
        var rebind = await CliProcess.InvokeAsync(
            workspace.Path,
            "mv",
            "docs/managed/template.md",
            "docs/rebinding/template.md"
        );
        await Assert.That(rebind.ExitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(reboundTarget)).IsEqualTo("version two");

        var uninstall = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "example");

        await Assert.That(uninstall.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(reboundTarget)).IsFalse();
    }

    [Test]
    public async Task PackLifecycle_WhenInstallRemapSaved_ReusesMappingOnFutureInstall()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateRemapSelectorPackSource(workspace.Path, "directory");
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "example",
            "--remap-directory",
            "docs/development=docs/04-development",
            "--save-remap"
        );
        await Assert.That(install.ExitCode).IsEqualTo(0);
        var uninstall = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "example");
        await Assert
            .That(uninstall.ExitCode)
            .IsEqualTo(0)
            .Because(uninstall.StandardOutput + uninstall.StandardError);

        var reinstall = await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        await Assert.That(reinstall.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "04-development", "root.txt")))
            .IsTrue();
    }

    [Test]
    public async Task PackLifecycle_WhenInstallIgnoreRemapSaved_OmitsTargetAndLockEntry()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateRemapSelectorPackSource(workspace.Path, "directory");
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "example",
            "--remap-directory",
            "docs/development=@ignore",
            "--save-remap"
        );
        var configuration = File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml"));
        var lockFile = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(Directory.Exists(Path.Combine(workspace.Path, "docs"))).IsFalse();
        await Assert.That(configuration).Contains("docs/development: '@ignore'");
        await Assert.That(lockFile).DoesNotContain("declaredTargetPath:");
    }

    [Test]
    public async Task PackLifecycle_WhenDirectoryMoveRemapSaved_ReusesMappingOnFutureInstall()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateRemapSelectorPackSource(workspace.Path, "directory");
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        await Assert
            .That((await CliProcess.InvokeAsync(workspace.Path, "install", "example")).ExitCode)
            .IsEqualTo(0);

        var move = await CliProcess.InvokeAsync(
            workspace.Path,
            "mv",
            "docs/development",
            "docs/04-development",
            "--save-remap"
        );
        await Assert.That(move.ExitCode).IsEqualTo(0);
        var uninstall = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "example");
        await Assert
            .That(uninstall.ExitCode)
            .IsEqualTo(0)
            .Because(uninstall.StandardOutput + uninstall.StandardError);

        var reinstall = await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        await Assert.That(reinstall.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "04-development", "root.txt")))
            .IsTrue();
    }

    [Test]
    [Arguments(
        "file",
        "",
        "",
        "--remap-file",
        "docs\\development\\root.txt=docs\\04-development\\root.txt",
        "docs/04-development/root.txt"
    )]
    [Arguments(
        "directory",
        "",
        "",
        "--remap-directory",
        "docs/development/=docs/04-development/",
        "docs/04-development/nested/child.txt;docs/04-development/root.txt"
    )]
    [Arguments(
        "glob",
        "",
        "",
        "--remap-file",
        "docs/development/nested/child.json=docs/special/child.json",
        "docs/development/root.json;docs/special/child.json"
    )]
    [Arguments(
        "directory",
        "docs\\development\\=docs\\configured\\",
        "",
        "",
        "",
        "docs/configured/nested/child.txt;docs/configured/root.txt"
    )]
    [Arguments(
        "glob",
        "",
        "docs/development/nested/child.json=docs/configured/child.json",
        "",
        "",
        "docs/configured/child.json;docs/development/root.json"
    )]
    [Arguments(
        "directory",
        "docs/development=docs/configured",
        "",
        "--remap-directory",
        "docs/development=docs/invocation",
        "docs/invocation/nested/child.txt;docs/invocation/root.txt"
    )]
    [Arguments(
        "directory",
        "",
        "docs/development/nested/child.txt=docs/configured/child.txt",
        "--remap-file",
        "docs/development/nested/child.txt=docs/invocation/child.txt",
        "docs/development/root.txt;docs/invocation/child.txt"
    )]
    [Arguments(
        "directory",
        "docs/development=docs/configured",
        "",
        "--remap-file",
        "docs/development/nested/child.txt=docs/invocation/child.txt",
        "docs/configured/root.txt;docs/invocation/child.txt"
    )]
    [Arguments(
        "directory",
        "docs/development=docs/configured",
        "docs/development/nested/child.txt=docs/special/child.txt",
        "",
        "",
        "docs/configured/root.txt;docs/special/child.txt"
    )]
    [Arguments(
        "directory",
        "",
        "docs/development/nested/child.txt=docs/configured/child.txt",
        "--remap-directory",
        "docs/development=docs/invocation",
        "docs/configured/child.txt;docs/invocation/root.txt"
    )]
    public async Task PackLifecycle_WhenRemappingVaries_WritesExpectedConcreteTargets(
        string selectorKind,
        string configuredDirectoryMapping,
        string configuredFileMapping,
        string installOption,
        string installMapping,
        string expectedTargets
    )
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateRemapSelectorPackSource(workspace.Path, selectorKind);
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        ConfigureRemapping(workspace.Path, configuredDirectoryMapping, configuredFileMapping);
        var arguments = new List<string> { "install", "example" };
        if (installOption.Length > 0)
        {
            arguments.Add(installOption);
            arguments.Add(installMapping);
        }

        var install = await CliProcess.InvokeAsync(workspace.Path, [.. arguments]);
        var targets = expectedTargets.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var lockFile = await File.ReadAllTextAsync(
            Path.Combine(workspace.Path, "lunapack-lock.yml")
        );
        var recordedTargets = lockFile
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("targetPath: ", StringComparison.Ordinal))
            .Select(line => line["targetPath: ".Length..])
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToList();

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(recordedTargets).IsEquivalentTo(targets);
        foreach (var target in targets)
        {
            await Assert.That(File.Exists(Path.Combine(workspace.Path, target))).IsTrue();
        }
    }

    [Test]
    public async Task Scenario_DotnetProjectManagedContentUnchanged_InstallsAndUninstalls()
    {
        using var workspace = new TestWorkspace();
        await InitializeAndAddSampleSourceAsync(workspace.Path);
        var buildPropsPath = Path.Combine(workspace.Path, "Directory.Build.props");
        var packagesPropsPath = Path.Combine(workspace.Path, "Directory.Packages.props");

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "dotnet-project");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(buildPropsPath)).IsTrue();
        await Assert.That(File.Exists(packagesPropsPath)).IsTrue();
        var packageVersions = File.ReadAllText(packagesPropsPath);
        await Assert.That(packageVersions).Contains("PackageVersion Include=\"CSharpier.MsBuild\"");
        await Assert.That(packageVersions).DoesNotContain("PackageVersion Include=\"NJsonSchema\"");

        var uninstall = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "dotnet-project");

        await Assert.That(uninstall.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(buildPropsPath)).IsFalse();
        await Assert.That(File.Exists(packagesPropsPath)).IsFalse();
    }

    [Test]
    public async Task Scenario_MadrDirectoryExists_InstallsTemplate()
    {
        using var workspace = new TestWorkspace();
        await InitializeAndAddSampleSourceAsync(workspace.Path);
        var adrDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "docs", "adr"));

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "madr-template");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(adrDirectory.FullName, "template.md"))).IsTrue();
    }

    [Test]
    public async Task PackLifecycle_WhenManagedContentModified_PreservesFileAndState()
    {
        using var workspace = new TestWorkspace();

        await InitializeAndAddSampleSourceAsync(workspace.Path);
        await CliProcess.InvokeAsync(workspace.Path, "install", "dotnet-gitignore");
        var managedFilePath = Path.Combine(workspace.Path, ".gitignore");
        File.AppendAllText(managedFilePath, "# user change\n");

        var uninstall = await CliProcess.InvokeAsync(
            workspace.Path,
            "uninstall",
            "dotnet-gitignore"
        );

        await Assert.That(uninstall.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(managedFilePath)).IsTrue();
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml")))
            .Contains("dotnet-gitignore");
    }

    [Test]
    public async Task CatalogCommands_WhenPacksAvailable_EmitCompactOrderedResults()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            ("exact-v1", "cli", "1.0.0", "first release", "exact"),
            ("exact-v2", "cli", "2.0.0", "second release", "exact"),
            ("exact-v3", "cli", "3.0.0", "third release", "exact"),
            ("exact-v4", "cli", "4.0.0", "fourth release", "exact"),
            ("prefix", "cli-pack", "1.0.0", null, "prefix"),
            ("substring", "pack-cli", "1.0.0", null, "substring"),
            ("description", "documentation", "1.0.0", "CLI reference", "description"),
            ("preview", "preview", "1.0.0", new string('a', 81), "preview")
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var discover = await CliProcess.InvokeAsync(workspace.Path, "discover");
        var search = await CliProcess.InvokeAsync(workspace.Path, "search", "cli");
        var expandedSearch = await CliProcess.InvokeAsync(
            workspace.Path,
            "search",
            "cli",
            "--versions",
            "4"
        );
        await Assert.That(discover.ExitCode).IsEqualTo(0);
        await Assert.That(discover.StandardOutput).Contains("preview");
        await Assert.That(discover.StandardOutput).Contains("1.0.0");
        await Assert.That(search.ExitCode).IsEqualTo(0);
        await Assert.That(search.StandardOutput).Contains("cli");
        await Assert.That(search.StandardOutput).Contains("4.0.0");
        await Assert.That(search.StandardOutput).Contains("fourth release");
        await Assert.That(search.StandardOutput).DoesNotContain("first release");
        await Assert.That(search.StandardOutput).Contains("cli-pack");
        await Assert.That(search.StandardOutput).Contains("pack-cli");
        await Assert.That(search.StandardOutput).Contains("CLI reference");
        await Assert.That(expandedSearch.ExitCode).IsEqualTo(0);
        await Assert.That(expandedSearch.StandardOutput).Contains("3.0.0");
        await Assert.That(expandedSearch.StandardOutput).Contains("third release");
        await Assert.That(expandedSearch.StandardOutput).Contains("2.0.0");
        await Assert.That(expandedSearch.StandardOutput).Contains("second release");
        await Assert.That(expandedSearch.StandardOutput).Contains("first release");
    }

    [Test]
    public async Task Search_WhenConfiguredLinkMatchesTerm_ListsLink()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = Directory.CreateDirectory(Path.Combine(workspace.Path, "source")).FullName;
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "CSharpExpert.agent.md"), "agent");
        await InitializeAndAddSourceAsync(workspace.Path, "source");
        var added = await CliProcess.InvokeAsync(
            workspace.Path,
            "links",
            "add",
            "csharp-agent",
            "--source",
            "local",
            "--include",
            "CSharpExpert.agent.md"
        );

        var search = await CliProcess.InvokeAsync(workspace.Path, "search", "csharp");

        await Assert.That(added.ExitCode).IsEqualTo(0);
        await Assert.That(search.ExitCode).IsEqualTo(0);
        await Assert.That(search.StandardOutput).Contains("csharp-agent");
        await Assert
            .That(search.StandardOutput)
            .Contains("Found 0 matching packs and 1 matching links");
    }

    [Test]
    public async Task Scenario_RepositorySourceConfigured_DiscoversAllBundledPacks()
    {
        var repositoryRoot = GetRepositoryRoot();

        var discover = await CliProcess.InvokeAsync(repositoryRoot, "discover");

        await Assert.That(discover.ExitCode).IsEqualTo(0);
        foreach (var (packId, version) in GetBundledPacks())
        {
            await Assert
                .That(discover.StandardOutput)
                .Contains(packId[..Math.Min(packId.Length, 20)]);
            await Assert.That(discover.StandardOutput).Contains(version);
        }
    }

    [Test]
    public async Task Scenario_BundledIgnorePacks_MergeMarkedSections()
    {
        using var workspace = new TestWorkspace();
        await InitializeAndAddSampleSourceAsync(workspace.Path);

        var dotnetInstall = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "dotnet-gitignore"
        );
        var generalInstall = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "gitignore-baseline"
        );
        var gitIgnore = File.ReadAllText(Path.Combine(workspace.Path, ".gitignore"));
        var lockFile = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));

        await Assert.That(dotnetInstall.ExitCode).IsEqualTo(0);
        await Assert.That(generalInstall.ExitCode).IsEqualTo(0);
        await Assert.That(gitIgnore).Contains("# BEGIN Lunapack .NET ignores");
        await Assert.That(gitIgnore).Contains("# END Lunapack .NET ignores");
        await Assert.That(gitIgnore).Contains("# BEGIN Lunapack general ignores");
        await Assert.That(gitIgnore).Contains("# END Lunapack general ignores");
        await Assert.That(lockFile).Contains("id: dotnet-gitignore");
        await Assert.That(lockFile).Contains("id: gitignore-baseline");
    }

    [Test]
    public async Task Scenario_UpdateCommands_ReportOutdatedAndApplyNamedRelease()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            ("one", "example", "1.0.0", null, "one"),
            ("two", "example", "1.1.0", null, "two")
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        await CliProcess.InvokeAsync(workspace.Path, "install", "example@1.0.0");
        var initialState = string.Concat(
            File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml")),
            File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"))
        );

        var outdated = await CliProcess.InvokeAsync(workspace.Path, "outdated");

        await Assert.That(outdated.ExitCode).IsEqualTo(0);
        await Assert.That(outdated.StandardOutput).Contains("example");
        await Assert.That(outdated.StandardOutput).Contains("1.0.0");
        await Assert.That(outdated.StandardOutput).Contains("1.1.0");
        await Assert
            .That(
                string.Concat(
                    File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml")),
                    File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"))
                )
            )
            .IsEqualTo(initialState);

        var update = await CliProcess.InvokeAsync(workspace.Path, "update", "example");

        await Assert.That(update.ExitCode).IsEqualTo(0);
        await Assert.That(update.StandardOutput).Contains("Updated 'example' (version '1.1.0')");
        await Assert.That(File.ReadAllText(Path.Combine(workspace.Path, ".pack"))).IsEqualTo("two");
    }

    [Test]
    public async Task Scenario_RepositoryPacksInstalledWithOverlays_RejectsDuplicateInstallation()
    {
        var repositoryRoot = GetRepositoryRoot();
        var editorConfigPath = Path.Combine(repositoryRoot, ".editorconfig");
        var gitIgnorePath = Path.Combine(repositoryRoot, ".gitignore");
        var manifestPath = Path.Combine(repositoryRoot, "lunapack.yml");
        var packageVersionsPath = Path.Combine(repositoryRoot, "Directory.Packages.props");
        var initialEditorConfig = File.ReadAllText(editorConfigPath);
        var initialGitIgnore = File.ReadAllText(gitIgnorePath);
        var initialManifest = File.ReadAllText(manifestPath);
        var packageVersions = File.ReadAllText(packageVersionsPath);

        var install = await CliProcess.InvokeAsync(
            repositoryRoot,
            "install",
            "dotnet-editorconfig"
        );

        await Assert.That(install.ExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(editorConfigPath)).IsEqualTo(initialEditorConfig);
        await Assert.That(File.ReadAllText(gitIgnorePath)).IsEqualTo(initialGitIgnore);
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(initialManifest);
        await Assert.That(packageVersions).DoesNotContain("PackageVersion Include=\"NJsonSchema\"");
        foreach (var packId in GetConfiguredRootPackIds())
        {
            await Assert.That(initialManifest).Contains($"id: {packId}");
        }
    }

    [Test]
    public async Task Discover_WhenNoSourcesConfigured_ReturnsFailureWithDiagnostic()
    {
        using var workspace = new TestWorkspace();
        await InitializeAndAddNoSourcesAsync(workspace.Path);

        var discover = await CliProcess.InvokeAsync(workspace.Path, "discover");

        await Assert.That(discover.ExitCode).IsEqualTo(1);
        await Assert.That(discover.StandardOutput).Contains("error: No sources are configured.");
    }

    [Test]
    public async Task Discover_WhenConfiguredSourcesContainNoPacks_ReturnsFailureWithDiagnostic()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        await InitializeAndAddSourceAsync(workspace.Path, "source");

        var discover = await CliProcess.InvokeAsync(workspace.Path, "discover");

        await Assert.That(discover.ExitCode).IsEqualTo(1);
        await Assert
            .That(discover.StandardOutput)
            .Contains("error: No packs were found in configured sources.");
    }

    [Test]
    public async Task Search_WhenNoPacksMatchTerm_ReturnsFailureWithDiagnostic()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            ("example", "example", "1.0.0", null, "example")
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var search = await CliProcess.InvokeAsync(workspace.Path, "search", "missing");

        await Assert.That(search.ExitCode).IsEqualTo(1);
        await Assert
            .That(search.StandardOutput)
            .Contains("error: No packs or links were found for 'missing'.");
    }

    [Test]
    public async Task Init_WhenWorkspaceProvided_CreatesManifestInWorkspace()
    {
        using var workspace = new TestWorkspace();
        var projectDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "sandbox"))
            .FullName;

        var init = await CliProcess.InvokeAsync(workspace.Path, "init", "--workspace", "sandbox");

        await Assert.That(init.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(projectDirectory, "lunapack.yml"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "lunapack.yml"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenVersionExplicit_InstallsRequestedRelease()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            ("one", "example", "1.0.0", null, "one"),
            ("two", "example", "2.0.0", null, "two")
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example@1.0.0");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(Path.Combine(workspace.Path, ".pack"))).IsEqualTo("one");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml")))
            .Contains("version: 1.0.0");
    }

    [Test]
    public async Task Install_WhenVersionOmitted_InstallsLatestRelease()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            ("one", "example", "1.0.0", null, "one"),
            ("two", "example", "2.0.0", null, "two")
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(Path.Combine(workspace.Path, ".pack"))).IsEqualTo("two");
    }

    [Test]
    public async Task Validate_WhenReferenceIncludesVersion_ValidatesRequestedRelease()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            ("one", "example", "1.0.0", null, "one"),
            ("two", "example", "2.0.0", null, "two")
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var validate = await CliProcess.InvokeAsync(workspace.Path, "validate", "example@1.0.0");

        await Assert.That(validate.ExitCode).IsEqualTo(0);
        await Assert.That(validate.StandardOutput).Contains("example@1.0.0 is valid.");
    }

    [Test]
    public async Task Inspect_WhenPackHasMetadataParametersAndReferences_FormatsEachSection()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateCompositePackSource(
            workspace.Path,
            (
                "shared",
                "id: shared\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: shared.txt\n",
                "shared"
            ),
            (
                "foundation",
                "id: foundation\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\ndescription: Foundation pack\nparameters:\n  companyName:\n    type: string\n    required: true\n    displayName: Company name\n    description: Legal entity name.\nmanagedFiles:\n  - source: templates/content.txt\n    target: foundation.txt\npacks:\n  - id: shared\n    version: 1.0.0\n",
                "foundation"
            )
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var inspect = await CliProcess.InvokeAsync(workspace.Path, "inspect", "foundation@1.0.0");

        await Assert.That(inspect.ExitCode).IsEqualTo(0);
        await Assert.That(inspect.StandardOutput).Contains("Foundation pack");
        await Assert.That(inspect.StandardOutput).Contains("MIT");
        await Assert.That(inspect.StandardOutput).Contains("Lunaris Digital Solutions");
        await Assert.That(inspect.StandardOutput).Contains("company");
        await Assert.That(inspect.StandardOutput).Contains("Company");
        await Assert.That(inspect.StandardOutput).Contains("Legal");
        await Assert.That(inspect.StandardOutput).Contains("entity");
        await Assert.That(inspect.StandardOutput).Contains("name.");
        await Assert.That(inspect.StandardOutput).Contains("Referenced packs");
        await Assert.That(inspect.StandardOutput).Contains("shared");
    }

    [Test]
    public async Task Inspect_WhenVersionOmitted_UsesLatestAndOmitsEmptyReferenceSection()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            ("one", "example", "1.0.0", "First release", "one"),
            ("two", "example", "2.0.0", "Latest release", "two")
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var inspect = await CliProcess.InvokeAsync(workspace.Path, "inspect", "example");

        await Assert.That(inspect.ExitCode).IsEqualTo(0);
        await Assert.That(inspect.StandardOutput).Contains("2.0.0");
        await Assert.That(inspect.StandardOutput).Contains("Latest release");
        await Assert.That(inspect.StandardOutput).DoesNotContain("Referenced packs");
    }

    [Test]
    public async Task Install_WhenLicenseParameterExplicit_RendersProvidedCompanyName()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateCompositePackSource(
            workspace.Path,
            (
                "license-mit",
                "id: license-mit\nversion: 1.0.0\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: LICENSE.md\n    template: true\n",
                "Copyright {{ companyName }}"
            )
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "license-mit",
            "--parameter",
            "companyName=Example Corporation"
        );

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "LICENSE.md")))
            .IsEqualTo("Copyright Example Corporation");
    }

    [Test]
    public async Task Install_WhenLicenseProjectVariableConfigured_RendersProjectCompanyName()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateCompositePackSource(
            workspace.Path,
            (
                "license-mit",
                "id: license-mit\nversion: 1.0.0\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: LICENSE.md\n    template: true\n",
                "Copyright {{ companyName }}"
            )
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        File.WriteAllText(
            configurationPath,
            File.ReadAllText(configurationPath)
                .Replace(
                    "variables: {}",
                    "variables:\n  companyName: Project Corporation",
                    StringComparison.Ordinal
                )
        );

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "license-mit");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "LICENSE.md")))
            .IsEqualTo("Copyright Project Corporation");
    }

    [Test]
    public async Task Install_WhenCompositePacksShareParameter_BindsSingleInputForEveryPack()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateCompositePackSource(
            workspace.Path,
            (
                "shared",
                "id: shared\nversion: 1.0.0\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: shared.txt\n    template: true\n",
                "Shared {{ companyName }}"
            ),
            (
                "foundation",
                "id: foundation\nversion: 1.0.0\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: foundation.txt\n    template: true\npacks:\n  - id: shared\n    version: 1.0.0\n",
                "Foundation {{ companyName }}"
            )
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var install = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "foundation",
            "-p",
            "companyName=Lunaris"
        );

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "foundation.txt")))
            .IsEqualTo("Foundation Lunaris");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "shared.txt")))
            .IsEqualTo("Shared Lunaris");
    }

    [Test]
    public async Task Install_WhenRequestedVersionUnavailable_LeavesProjectUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path, ("one", "example", "1.0.0", null, "one"));
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        var manifestPath = Path.Combine(workspace.Path, "lunapack.yml");
        var initialManifest = File.ReadAllText(manifestPath);

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example@2.0.0");

        await Assert.That(install.ExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(initialManifest);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".pack"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenCompositeContentless_InstallsDependencyAndLocksCompleteGraph()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateCompositePackSource(
            workspace.Path,
            (
                "shared",
                "id: shared\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: shared.txt\n",
                "shared"
            ),
            (
                "foundation",
                "id: foundation\nversion: 1.0.0\npacks:\n  - id: shared\n    version: 1.0.0\n",
                null
            )
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "foundation");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "shared.txt"))).IsTrue();
        var configuration = File.ReadAllText(Path.Combine(workspace.Path, "lunapack.yml"));
        var lockFile = File.ReadAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"));
        await Assert.That(configuration).Contains("id: foundation");
        await Assert.That(configuration).DoesNotContain("id: shared");
        await Assert.That(lockFile).Contains("id: foundation");
        await Assert.That(lockFile).Contains("id: shared");
    }

    [Test]
    public async Task Scenario_PlatformCompositionReferences_ResolveFromConsumerSource()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateCompositePackSource(
            workspace.Path,
            (
                "azure-bicep",
                "id: azure-bicep\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: infrastructure/main.bicep\n",
                "bicep"
            ),
            (
                "github-actions",
                "id: github-actions\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: .github/workflows/ci.yml\n",
                "actions"
            ),
            (
                "aspnetcore",
                "id: aspnetcore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: src/Api/Api.csproj\n",
                "aspnetcore"
            ),
            (
                "angular",
                "id: angular\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: src/web/package.json\n",
                "angular"
            ),
            (
                "platform-composite",
                "id: platform-composite\nversion: 1.0.0\npacks:\n  - id: azure-bicep\n    version: 1.0.0\n  - id: github-actions\n    version: 1.0.0\n  - id: aspnetcore\n    version: 1.0.0\n  - id: angular\n    version: 1.0.0\n",
                null
            )
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "infrastructure"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".github", "workflows"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "src", "Api"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "src", "web"));

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "platform-composite");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "infrastructure", "main.bicep")))
            .IsTrue();
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, ".github", "workflows", "ci.yml")))
            .IsTrue();
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "src", "Api", "Api.csproj")))
            .IsTrue();
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "src", "web", "package.json")))
            .IsTrue();
    }

    [Test]
    public async Task Install_WhenCompositeMixed_InstallsOwnedAndDependencyFiles()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateCompositePackSource(
            workspace.Path,
            (
                "shared",
                "id: shared\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: shared.txt\n",
                "shared"
            ),
            (
                "foundation",
                "id: foundation\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: foundation.txt\npacks:\n  - id: shared\n    version: 1.0.0\n",
                "foundation"
            )
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "foundation");

        await Assert.That(install.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "foundation.txt"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "shared.txt"))).IsTrue();
    }

    [Test]
    public async Task Uninstall_WhenDependencyShared_RetainsItUntilLastRootRemoved()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateCompositePackSource(
            workspace.Path,
            (
                "shared",
                "id: shared\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: shared.txt\n",
                "shared"
            ),
            (
                "first",
                "id: first\nversion: 1.0.0\npacks:\n  - id: shared\n    version: 1.0.0\n",
                null
            ),
            (
                "second",
                "id: second\nversion: 1.0.0\npacks:\n  - id: shared\n    version: 1.0.0\n",
                null
            )
        );
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        await CliProcess.InvokeAsync(workspace.Path, "install", "first");
        await CliProcess.InvokeAsync(workspace.Path, "install", "second");

        var firstRemoval = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "first");

        await Assert.That(firstRemoval.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "shared.txt"))).IsTrue();

        var secondRemoval = await CliProcess.InvokeAsync(workspace.Path, "uninstall", "second");

        await Assert.That(secondRemoval.ExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "shared.txt"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenStateWriteFails_RollsBackCopiedFilesAndDocuments()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path, ("one", "example", "1.0.0", null, "one"));
        await InitializeAndAddSourceAsync(workspace.Path, sourcePath);
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        var lockFilePath = Path.Combine(workspace.Path, "lunapack-lock.yml");
        var initialConfiguration = File.ReadAllText(configurationPath);
        var initialLockFile = File.ReadAllText(lockFilePath);
        using var lockStream = File.Open(
            lockFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );

        var install = await CliProcess.InvokeAsync(workspace.Path, "install", "example");

        await Assert.That(install.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".pack"))).IsFalse();
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
        await Assert.That(File.ReadAllText(lockFilePath)).IsEqualTo(initialLockFile);
    }

    private static async Task InitializeAndAddSampleSourceAsync(string projectDirectory) =>
        await InitializeAndAddSourceAsync(
            projectDirectory,
            CopySamplePackSourceToWorkspace(projectDirectory)
        );

    private static async Task InitializeAndAddNoSourcesAsync(string projectDirectory)
    {
        var init = await CliProcess.InvokeAsync(projectDirectory, "init");

        await Assert.That(init.ExitCode).IsEqualTo(0);
    }

    private static async Task InitializeAndAddSourceAsync(
        string projectDirectory,
        string sourcePath
    )
    {
        var init = await CliProcess.InvokeAsync(projectDirectory, "init");
        var source = await CliProcess.InvokeAsync(
            projectDirectory,
            "sources",
            "add",
            "local",
            "local",
            sourcePath
        );

        await Assert.That(init.ExitCode).IsEqualTo(0);
        await Assert.That(source.ExitCode).IsEqualTo(0);
    }

    private static void ConfigureRemapping(
        string projectDirectory,
        string directoryMapping,
        string fileMapping
    )
    {
        if (directoryMapping.Length == 0 && fileMapping.Length == 0)
        {
            return;
        }

        var configuration = new StringBuilder("\nremap:\n");
        AppendMapping(configuration, "directories", directoryMapping);
        AppendMapping(configuration, "files", fileMapping);
        File.AppendAllText(
            Path.Combine(projectDirectory, "lunapack.yml"),
            configuration.ToString()
        );
    }

    private static void AppendMapping(StringBuilder configuration, string name, string mapping)
    {
        configuration.Append("  ").Append(name).Append(':');
        if (mapping.Length == 0)
        {
            configuration.Append(" {}\n");
            return;
        }

        var separatorIndex = mapping.IndexOf('=', StringComparison.Ordinal);
        configuration
            .Append("\n    '")
            .Append(mapping.AsSpan(0, separatorIndex))
            .Append("': '")
            .Append(mapping.AsSpan(separatorIndex + 1))
            .Append("'\n");
    }

    private static string CreateRemapSelectorPackSource(
        string projectDirectory,
        string selectorKind
    )
    {
        var sourcePath = Directory
            .CreateDirectory(Path.Combine(projectDirectory, "source"))
            .FullName;
        var packDirectory = Directory.CreateDirectory(Path.Combine(sourcePath, "example")).FullName;
        var templatesDirectory = Directory
            .CreateDirectory(Path.Combine(packDirectory, "templates"))
            .FullName;
        var selector = selectorKind switch
        {
            "file" => "source: templates/root.txt",
            "directory" => "directory: templates/directory",
            _ => "glob: templates/glob/**/*.json",
        };
        var target = string.Equals(selectorKind, "file", StringComparison.Ordinal)
            ? "docs/development/root.txt"
            : "docs/development/";
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            $"id: example\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - {selector}\n    target: {target}\n"
        );

        if (string.Equals(selectorKind, "file", StringComparison.Ordinal))
        {
            File.WriteAllText(Path.Combine(templatesDirectory, "root.txt"), "root");
            return "source";
        }

        var contentDirectory = Directory
            .CreateDirectory(
                Path.Combine(
                    templatesDirectory,
                    string.Equals(selectorKind, "directory", StringComparison.Ordinal)
                        ? "directory"
                        : "glob"
                )
            )
            .FullName;
        var extension = string.Equals(selectorKind, "directory", StringComparison.Ordinal)
            ? "txt"
            : "json";
        var nestedDirectory = Directory
            .CreateDirectory(Path.Combine(contentDirectory, "nested"))
            .FullName;
        File.WriteAllText(Path.Combine(contentDirectory, $"root.{extension}"), "root");
        File.WriteAllText(Path.Combine(nestedDirectory, $"child.{extension}"), "child");
        return "source";
    }

    private static string CreatePackSource(
        string projectDirectory,
        params (
            string Directory,
            string Id,
            string Version,
            string? Description,
            string Contents
        )[] packs
    )
    {
        var sourcePath = Directory
            .CreateDirectory(Path.Combine(projectDirectory, "source"))
            .FullName;
        foreach (var pack in packs)
        {
            var descriptionLine = pack.Description is null
                ? null
                : $"description: {pack.Description}\n";
            var packDirectory = Directory
                .CreateDirectory(Path.Combine(sourcePath, pack.Directory))
                .FullName;
            var templatesDirectory = Directory
                .CreateDirectory(Path.Combine(packDirectory, "templates"))
                .FullName;
            File.WriteAllText(
                Path.Combine(packDirectory, "pack.yml"),
                $"id: {pack.Id}\nversion: {pack.Version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\n{descriptionLine}managedFiles:\n  - source: templates/content.txt\n    target: .pack\n"
            );
            File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), pack.Contents);
        }

        return "source";
    }

    private static string CreateInstructionPackSource(
        string projectDirectory,
        string directory,
        string manifest,
        IReadOnlyDictionary<string, string> instructions
    )
    {
        CreateInstructionPack(
            Path.Combine(projectDirectory, "source"),
            directory,
            manifest,
            instructions
        );
        return "source";
    }

    private static void CreateInstructionPack(
        string sourceRoot,
        string directory,
        string manifest,
        IReadOnlyDictionary<string, string> instructions,
        string managedContent = "managed"
    )
    {
        var packDirectory = Directory.CreateDirectory(Path.Combine(sourceRoot, directory)).FullName;
        var templatesDirectory = Directory
            .CreateDirectory(Path.Combine(packDirectory, "templates"))
            .FullName;
        File.WriteAllText(Path.Combine(packDirectory, "pack.yml"), AddRequiredMetadata(manifest));
        File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), managedContent);
        if (instructions.Count == 0)
        {
            return;
        }

        var instructionsDirectory = Directory
            .CreateDirectory(Path.Combine(packDirectory, "instructions"))
            .FullName;
        foreach (var (name, content) in instructions)
        {
            File.WriteAllText(Path.Combine(instructionsDirectory, name), content);
        }
    }

    private static string CreateRemappableVersionedPackSource(string projectDirectory)
    {
        var sourcePath = Directory
            .CreateDirectory(Path.Combine(projectDirectory, "source"))
            .FullName;
        foreach (
            var (directory, version, contents) in new[]
            {
                ("example-v1", "1.0.0", "version one"),
                ("example-v2", "2.0.0", "version two"),
            }
        )
        {
            var packDirectory = Directory
                .CreateDirectory(Path.Combine(sourcePath, directory))
                .FullName;
            var templatesDirectory = Directory
                .CreateDirectory(Path.Combine(packDirectory, "templates"))
                .FullName;
            File.WriteAllText(
                Path.Combine(packDirectory, "pack.yml"),
                $"id: example\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: docs/adr/template.md\n"
            );
            File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), contents);
        }

        return "source";
    }

    private static async Task<GitPackSource> CreateGitPackSourceAsync(
        params (string Directory, string Manifest, string? TemplateContents)[] additionalPacks
    )
    {
        var repositoryPath = Path.Combine(
            Path.GetTempPath(),
            "lunapack-tests",
            "git-sources",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(repositoryPath);
        var packDirectory = Directory
            .CreateDirectory(Path.Combine(repositoryPath, "packs", "example"))
            .FullName;
        var templatesDirectory = Directory
            .CreateDirectory(Path.Combine(packDirectory, "templates"))
            .FullName;
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            "id: example\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: .pack\n"
        );
        File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), "from git");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "excluded", "other"));
        File.WriteAllText(
            Path.Combine(repositoryPath, "excluded", "other", "pack.yml"),
            "id: excluded\nversion: 1.0.0\nmanagedFiles:\n  - source: missing.txt\n    target: excluded.txt\n"
        );
        foreach (var pack in additionalPacks)
        {
            CreateGitPack(repositoryPath, pack);
        }

        await GitProcess.InvokeAsync(repositoryPath, "init", "--initial-branch=main");
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
            "Initial pack source"
        );

        return new GitPackSource(repositoryPath);
    }

    private static void CreateGitPack(
        string repositoryPath,
        (string Directory, string Manifest, string? TemplateContents) pack
    )
    {
        var packDirectory = Directory
            .CreateDirectory(Path.Combine(repositoryPath, "packs", pack.Directory))
            .FullName;
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            AddRequiredMetadata(pack.Manifest)
        );
        if (pack.TemplateContents is not { } templateContents)
        {
            return;
        }

        var templatesDirectory = Directory
            .CreateDirectory(Path.Combine(packDirectory, "templates"))
            .FullName;
        File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), templateContents);
    }

    private sealed class GitPackSource(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            foreach (
                var filePath in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
            )
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
    }

    private static string CreateCompositePackSource(
        string projectDirectory,
        params (string Directory, string Manifest, string? TemplateContents)[] packs
    )
    {
        var sourcePath = Directory
            .CreateDirectory(Path.Combine(projectDirectory, "source"))
            .FullName;
        foreach (var pack in packs)
        {
            var packDirectory = Directory
                .CreateDirectory(Path.Combine(sourcePath, pack.Directory))
                .FullName;
            File.WriteAllText(
                Path.Combine(packDirectory, "pack.yml"),
                AddRequiredMetadata(pack.Manifest)
            );
            if (pack.TemplateContents is not { } templateContents)
            {
                continue;
            }

            var templatesDirectory = Directory
                .CreateDirectory(Path.Combine(packDirectory, "templates"))
                .FullName;
            File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), templateContents);
        }

        return "source";
    }

    private static string GetSamplePackSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "projects", "packs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the sample pack source.");
    }

    private static string CopySamplePackSourceToWorkspace(string projectDirectory)
    {
        const string sourceDirectoryName = "sample-packs";
        var sourcePath = GetSamplePackSourcePath();
        var destinationPath = Path.Combine(projectDirectory, sourceDirectoryName);
        Directory.CreateDirectory(destinationPath);

        foreach (
            var sourceFile in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
        )
        {
            var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationFile);
            if (destinationDirectory is null)
            {
                throw new InvalidOperationException(
                    $"Unable to determine destination directory for '{destinationFile}'."
                );
            }

            Directory.CreateDirectory(destinationDirectory);
            File.Copy(sourceFile, destinationFile);
        }

        return sourceDirectoryName;
    }

    private static string AddRequiredMetadata(string manifest)
    {
        if (manifest.Contains("\nlicense:", StringComparison.Ordinal))
        {
            return manifest;
        }

        var versionLineEnd = manifest.IndexOf(
            '\n',
            manifest.IndexOf("version:", StringComparison.Ordinal)
        );
        return manifest.Insert(
            versionLineEnd + 1,
            "license: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\n"
        );
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var packsDirectory = Path.Combine(directory.FullName, "projects", "packs");
            if (Directory.Exists(packsDirectory))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root.");
    }

    private static IReadOnlyList<(string Id, string Version)> GetBundledPacks() =>
        [
            ("clean-code-guidelines", "1.0.0"),
            ("commitlint", "1.0.0"),
            ("csharp-guidelines", "1.0.0"),
            ("dotnet-build-config", "1.0.0"),
            ("dotnet-central-package-management", "1.0.0"),
            ("dotnet-coding-guidance", "1.0.0"),
            ("dotnet-csharp-editorconfig", "1.0.0"),
            ("csharpier", "1.0.0"),
            ("dotnet-editorconfig", "1.0.0"),
            ("dotnet-gitignore", "1.0.0"),
            ("dotnet-project", "1.0.0"),
            ("dotnet-repository", "1.0.0"),
            ("dotnet-sdk-10", "1.0.0"),
            ("vscode-dotnet-workspace", "1.0.0"),
            ("editorconfig-baseline", "1.0.0"),
            ("github-commitlint-workflow", "1.0.0"),
            ("github-pull-request-gate-workflow", "1.0.0"),
            ("github-community-health", "1.0.0"),
            ("github-copilot-documentation-instructions", "1.0.0"),
            ("github-copilot-dotnet-instructions", "1.0.0"),
            ("github-copilot-setup-workflow", "1.0.0"),
            ("github-issue-forms", "1.0.0"),
            ("github-open-source-baseline", "1.0.0"),
            ("github-pull-request-quality", "1.0.0"),
            ("gitignore-baseline", "1.0.0"),
            ("husky", "1.0.0"),
            ("husky-lint-staged", "1.0.0"),
            ("husky-lint-staged-dotnet-quality", "1.0.0"),
            ("license-mit", "1.0.0"),
            ("lint-staged", "1.0.0"),
            ("lint-staged-csharp", "1.0.0"),
            ("lint-staged-css", "1.0.0"),
            ("lint-staged-dotnet-quality", "1.0.0"),
            ("lint-staged-html", "1.0.0"),
            ("lint-staged-json", "1.0.0"),
            ("lint-staged-markdown", "1.0.0"),
            ("lint-staged-markdownlint", "1.0.0"),
            ("lint-staged-quality", "1.0.0"),
            ("lint-staged-scss", "1.0.0"),
            ("lint-staged-yaml", "1.0.0"),
            ("madr-template", "1.0.0"),
            ("markdownlint-config", "1.0.0"),
            ("lunapack-pack-authoring", "1.0.0"),
            ("prettier-config", "1.0.0"),
            ("repository-contribution-guide", "1.0.0"),
            ("repository-documentation-quality", "1.0.0"),
        ];

    private static IReadOnlyList<string> GetConfiguredRootPackIds() =>
        [
            "clean-code-guidelines",
            "csharp-guidelines",
            "csharpier",
            "dotnet-editorconfig",
            "dotnet-gitignore",
            "gitignore-baseline",
            "dotnet-project",
            "dotnet-sdk-10",
            "dotnet-repository",
            "github-commitlint-workflow",
            "github-pull-request-gate-workflow",
            "github-copilot-setup-workflow",
            "github-pull-request-quality",
            "husky-lint-staged-dotnet-quality",
            "license-mit",
            "madr-template",
        ];
}
