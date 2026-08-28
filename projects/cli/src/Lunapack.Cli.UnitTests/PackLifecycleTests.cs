using System.Security.Cryptography;
using System.Text;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests;

public sealed class PackLifecycleTests
{
    [Test]
    public async Task Install_WhenPackUnavailable_LeavesManifestUnchanged()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var manifestPath = GetManifestPath(workspace.Path);
        var initialManifest = File.ReadAllText(manifestPath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(initialManifest);
    }

    [Test]
    public async Task Install_WhenScriptsRun_ExecutesPreAndPostHooksAroundManagedFiles()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            $"id: dotnet-gitignore\nversion: 1.0.0\nhooks:\n  preInstall:\n    - type: script\n      command: {ShellExecutable}\n      arguments:\n        - {ShellArgument}\n        - 'echo pre > lifecycle.txt'\n  postInstall:\n    - type: script\n      command: {ShellExecutable}\n      arguments:\n        - {ShellArgument}\n        - 'echo post >> lifecycle.txt'\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--scripts", "run"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        var lifecycleOutput = string.Join(
            "|",
            File.ReadAllLines(Path.Combine(workspace.Path, "lifecycle.txt"))
                .Select(line => line.Trim())
        );
        await Assert.That(lifecycleOutput).IsEqualTo("pre|post");
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsTrue();
    }

    [Test]
    public async Task Install_WhenScriptsSkip_DoesNotExecuteHooks()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            $"id: dotnet-gitignore\nversion: 1.0.0\nhooks:\n  preInstall:\n    - type: script\n      command: {ShellExecutable}\n      arguments:\n        - {ShellArgument}\n        - 'echo hook > lifecycle.txt'\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--scripts", "skip"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "lifecycle.txt"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsTrue();
    }

    [Test]
    public async Task Install_WhenScriptsDenied_WarnsBeforeHooksAndContinuesWithoutScripts()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = false;
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreatePackSource(
            workspace.Path,
            $"id: dotnet-gitignore\nversion: 1.0.0\nhooks:\n  preInstall:\n    - type: script\n      command: {ShellExecutable}\n      arguments:\n        - {ShellArgument}\n        - 'echo pre > lifecycle.txt'\n    - type: instruction\n      file: instructions/setup.md\n  postInstall:\n    - type: script\n      command: {ShellExecutable}\n      arguments:\n        - {ShellArgument}\n        - 'echo post >> lifecycle.txt'\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        var instructionsDirectory = Path.Combine(
            workspace.Path,
            sourcePath,
            "dotnet-gitignore",
            "instructions"
        );
        Directory.CreateDirectory(instructionsDirectory);
        File.WriteAllText(
            Path.Combine(instructionsDirectory, "setup.md"),
            "## Setup\nretained-instruction"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(
            ["trust", "scripts", "deny", "--project"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--scripts", "run"],
            workspace.Path
        );
        var output = ansiConsole.Output;
        var preWarning = output.IndexOf("event preInstall", StringComparison.Ordinal);
        var postWarning = output.IndexOf("event postInstall", StringComparison.Ordinal);
        var instruction = output.IndexOf("retained-instruction", StringComparison.Ordinal);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(preWarning >= 0 && postWarning > preWarning).IsTrue();
        await Assert.That(instruction > postWarning).IsTrue();
        await Assert.That(output).Contains("scopes: project");
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "lifecycle.txt"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsTrue();
    }

    [Test]
    public async Task Install_WhenInstructionsSkipped_DoesNotValidateThemAndStillRunsScripts()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            $"id: dotnet-gitignore\nversion: 1.0.0\nhooks:\n  preInstall:\n    - type: instruction\n      file: instructions/missing.md\n    - type: script\n      command: {ShellExecutable}\n      arguments:\n        - {ShellArgument}\n        - 'echo ran > lifecycle.txt'\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--scripts", "run", "--skip-instructions"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "lifecycle.txt"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsTrue();
    }

    [Test]
    public async Task Install_WhenPreHooksMixed_DispatchesThemInDeclarationOrder()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = false;
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreatePackSource(
            workspace.Path,
            $"id: dotnet-gitignore\nversion: 1.0.0\nhooks:\n  preInstall:\n    - type: instruction\n      file: instructions/first.md\n    - type: script\n      command: {ShellExecutable}\n      arguments:\n        - {ShellArgument}\n        - 'echo script-between'\n    - type: instruction\n      file: instructions/last.md\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        var instructionsDirectory = Path.Combine(
            workspace.Path,
            sourcePath,
            "dotnet-gitignore",
            "instructions"
        );
        Directory.CreateDirectory(instructionsDirectory);
        File.WriteAllText(Path.Combine(instructionsDirectory, "first.md"), "## First\nfirst-body");
        File.WriteAllText(Path.Combine(instructionsDirectory, "last.md"), "## Last\nlast-body");
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--scripts", "run"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        var first = ansiConsole.Output.IndexOf("first-body", StringComparison.Ordinal);
        var script = ansiConsole.Output.IndexOf("script-between", StringComparison.Ordinal);
        var last = ansiConsole.Output.IndexOf("last-body", StringComparison.Ordinal);
        await Assert.That(first >= 0 && first < script && script < last).IsTrue();
    }

    [Test]
    public async Task Install_WhenPostInstructionCancelled_RollsBackManagedFilesAndState()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nhooks:\n  postInstall:\n    - type: instruction\n      file: instructions/setup.md\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        var instructionsDirectory = Path.Combine(
            workspace.Path,
            sourcePath,
            "dotnet-gitignore",
            "instructions"
        );
        Directory.CreateDirectory(instructionsDirectory);
        File.WriteAllText(Path.Combine(instructionsDirectory, "setup.md"), "## Setup\nContinue");
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Install_WhenPreHookFails_DoesNotApplyManagedFilesOrPersistState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            $"id: dotnet-gitignore\nversion: 1.0.0\nhooks:\n  preInstall:\n    - type: script\n      command: {ShellExecutable}\n      arguments:\n        - {ShellArgument}\n        - {FailureCommand}\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--scripts", "run"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Install_WhenPostHookFails_RestoresManagedFilesAndProjectState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            $"id: dotnet-gitignore\nversion: 1.0.0\nhooks:\n  postInstall:\n    - type: script\n      command: {ShellExecutable}\n      arguments:\n        - {ShellArgument}\n        - {FailureCommand}\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--scripts", "run"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Install_WhenPostHookDeletesManifest_RestoresManifestAndRollsBack()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            $"id: dotnet-gitignore\nversion: 1.0.0\nhooks:\n  postInstall:\n    - type: script\n      command: {ShellExecutable}\n      arguments:\n        - {ShellArgument}\n        - {DeleteManifestCommand}\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var manifestPath = GetManifestPath(workspace.Path);
        var initialManifest = File.ReadAllText(manifestPath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--scripts", "run"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(initialManifest);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
    }

    [Test]
    public async Task Install_MultipleRootsWithSharedTransient_LocksTransientOnce()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateSharedTransientPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "root-one", "root-two"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(state.RequireValue().Configuration.Packs.Select(pack => pack.Id))
            .IsEquivalentTo(["root-one", "root-two"]);
        await Assert
            .That(state.RequireValue().LockFile.Packs.Select(pack => pack.Id))
            .IsEquivalentTo(["root-one", "root-two", "shared"]);
        await Assert
            .That(state.RequireValue().LockFile.Packs.Select(pack => pack.SourceName!))
            .IsEquivalentTo(["local", "local", "local"]);
        await Assert
            .That(state.RequireValue().LockFile.Packs.Select(pack => pack.SourceIdentity!.Path!))
            .IsEquivalentTo(["source", "source", "source"]);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "shared.txt")))
            .IsEqualTo("shared");
    }

    [Test]
    public async Task Install_MultipleReferencesWithInstalledRoot_WarnsAndContinues()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreateSharedTransientPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "root-one"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "root-one", "root-two"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(ansiConsole.Output)
            .Contains("warning: Pack 'root-one' is already installed.");
        await Assert
            .That(state.RequireValue().Configuration.Packs.Select(pack => pack.Id))
            .IsEquivalentTo(["root-one", "root-two"]);
    }

    [Test]
    public async Task Uninstall_MultipleRootsWithSharedTransient_RemovesCompleteGraph()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateSharedTransientPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "root-one", "root-two"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["uninstall", "root-one", "root-two"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(state.RequireValue().Configuration.Packs).IsEmpty();
        await Assert.That(state.RequireValue().LockFile.Packs).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "shared.txt"))).IsFalse();
    }

    [Test]
    public async Task Scenario_InstallDryRun_PreservesManagedTargetAndProjectState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);
        var targetPath = Path.Combine(workspace.Path, ".gitignore");

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "-D", "-d", "preview", "-nv"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(targetPath)).IsFalse();
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    [Arguments("local-user")]
    [Arguments("project")]
    [Arguments("global-user")]
    public async Task InstallDryRun_WhenScriptsDenied_ReportsSelectedScopeWithoutExecutionWarning(
        string scopeName
    )
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = false;
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nhooks:\n  preInstall:\n    - type: script\n      command: command-that-must-not-be-resolved\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var denyArguments = scopeName switch
        {
            "project" => new[] { "trust", "scripts", "deny", "--project" },
            "global-user" => new[] { "trust", "scripts", "deny", "--global" },
            _ => new[] { "trust", "scripts", "deny" },
        };
        await workspace.Application.RunAsync(denyArguments, workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "-D", "--scripts", "run"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains($"policy-denied scopes: {scopeName}");
        await Assert.That(ansiConsole.Output).DoesNotContain("Lifecycle script denied by policy");
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenRequiredParameterUsesShortOption_Succeeds()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "-p", "companyName=Lunaris"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task Install_WhenPackManifestContainsWindowsPaths_UsesCanonicalLockPaths()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: 'templates\\dotnet.gitignore'\n    target: 'docs\\adr\\template.md'\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );
        var lockFile = File.ReadAllText(
            Path.Combine(workspace.Path, ProjectStateStore.LockFileName)
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "docs", "adr", "template.md")))
            .IsEqualTo("bin/\nobj/\n");
        await Assert.That(lockFile).Contains("docs/adr/template.md");
        await Assert.That(lockFile).DoesNotContain("\\");
    }

    [Test]
    public async Task Install_WhenRequiredParameterMissing_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenMultiSelectParameterContainsDuplicate_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nparameters:\n  features:\n    type: enum\n    multiple: true\n    values: [api, docker]\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "-p", "features=api", "-p", "features=api"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenTemplateRendered_WritesRenderedContentAndDigest()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            RequiredCompanyParameterManifest,
            "{{ companyName }}"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "-p", "companyName=Lunaris Digital Solutions"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".gitignore")))
            .IsEqualTo("Lunaris Digital Solutions");
        await Assert
            .That(state.RequireValue().LockFile.Packs.Single().ManagedFiles.Single().Sha256)
            .IsEqualTo(
                Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes("Lunaris Digital Solutions"))
                )
            );
    }

    [Test]
    public async Task Install_WhenManagedFileReferenceMissing_WarnsAndWritesFallback()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: docs/index.md\n    template: true\n",
            "{{ files.path 'docs/missing.md' }}"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "docs", "index.md")))
            .IsEqualTo("docs/missing.md");
        await Assert
            .That(ansiConsole.Output)
            .Contains(
                "warning: Managed file target 'docs/missing.md' could not be resolved while rendering 'docs/index.md'."
            );
    }

    [Test]
    public async Task InstallDryRun_WhenManagedFileReferenceMissing_WarnsWithoutMutation()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: docs/index.md\n    template: true\n",
            "{{ files.relative_path 'docs/missing.md' }}"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--dry-run"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "docs", "index.md"))).IsFalse();
        await Assert
            .That(ansiConsole.Output)
            .Contains(
                "warning: Managed file target 'docs/missing.md' could not be resolved while rendering 'docs/index.md'."
            );
    }

    [Test]
    public async Task ManagedFilePathResolution_InstallUpdateAndDryRun_UsesSameEffectiveTargets()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedManagedPathTemplateSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(
            ["remap", "set", "file", "docs/index.md", ".github/agents/index.md"],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["remap", "set", "file", "docs/ref.md", "handbook/ref.md"],
            workspace.Path
        );
        var service = CreatePackLifecycleService(workspace);

        var installPreview = await service.DryRunInstallAsync(
            workspace.Path,
            new PackInstallationRequest(new PackReference("dotnet-gitignore", "1.0.0"), null, false)
        );
        var installPreviewContents = GetPlannedContents(
            installPreview.RequireValue().UpdatePlan,
            ".github/agents/index.md"
        );
        await workspace.Application.RunAsync(["install", "dotnet-gitignore@1.0.0"], workspace.Path);
        var installedContents = File.ReadAllText(
            Path.Combine(workspace.Path, ".github", "agents", "index.md")
        );

        var updatePreview = await service.DryRunUpdateAsync(
            workspace.Path,
            [new ProjectConfiguration.RequestedPack { Id = "dotnet-gitignore", Version = "2.0.0" }],
            new PackInstallationRequest(new PackReference("dotnet-gitignore", "2.0.0"), null, false)
        );
        var updatePreviewContents = GetPlannedContents(
            updatePreview.RequireValue(),
            ".github/agents/index.md"
        );
        await workspace.Application.RunAsync(["update", "dotnet-gitignore@2.0.0"], workspace.Path);

        await Assert
            .That(installPreviewContents)
            .IsEqualTo("v1:handbook/ref.md|../../handbook/ref.md");
        await Assert.That(installedContents).IsEqualTo(installPreviewContents);
        await Assert
            .That(updatePreviewContents)
            .IsEqualTo("v2:handbook/ref.md|../../handbook/ref.md");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".github", "agents", "index.md")))
            .IsEqualTo(updatePreviewContents);
    }

    [Test]
    public async Task Install_WhenExistingTargetMatchesRenderedContent_AdoptsTarget()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            RequiredCompanyParameterManifest,
            "{{ companyName }}"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var targetPath = Path.Combine(workspace.Path, ".gitignore");
        File.WriteAllText(targetPath, "Lunaris Digital Solutions");

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "-a", "-p", "companyName=Lunaris Digital Solutions"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("Lunaris Digital Solutions");
    }

    [Test]
    public async Task Install_WhenConditionFalse_OmitsTargetAndLockOwnership()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nparameters:\n  includeCi:\n    type: bool\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n    condition: includeCi\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        await Assert.That(state.RequireValue().LockFile.Packs.Single().ManagedFiles).IsEmpty();
    }

    [Test]
    public async Task Install_WhenTemplateInvalid_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n    template: true\n",
            templateContents: "{{ unknownVariable }}"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenConditionInvalid_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n    condition: undeclared\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenMultiSelectUsesScalarCondition_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nparameters:\n  features:\n    type: enum\n    multiple: true\n    values: [api, docker]\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n    condition: 'features == \"docker\"'\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "-p", "features=docker"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenRenderedManifestWriteFails_RollsBackTarget()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            RequiredCompanyParameterManifest,
            "{{ companyName }}"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await CreatePackLifecycleService(
                workspace,
                new FailingProjectStateStore(workspace.StateStore)
            )
            .InstallAsync(
                workspace.Path,
                new PackInstallationRequest(
                    new PackReference("dotnet-gitignore", null),
                    null,
                    false
                )
                {
                    Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["companyName"] = "Lunaris Digital Solutions",
                    },
                }
            );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenPackManifestInvalid_LeavesProjectUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: invalid\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);
        var manifestPath = GetManifestPath(workspace.Path);
        var initialManifest = File.ReadAllText(manifestPath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(initialManifest);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenTargetUnowned_LeavesTargetAndManifestUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        var targetPath = Path.Combine(workspace.Path, ".gitignore");
        File.WriteAllText(targetPath, "# user owned\n");
        var manifestPath = GetManifestPath(workspace.Path);
        var initialManifest = File.ReadAllText(manifestPath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("# user owned\n");
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(initialManifest);
    }

    [Test]
    public async Task Install_WhenExistingTargetMatchesPackContent_AdoptsTarget()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        var targetPath = Path.Combine(workspace.Path, ".gitignore");
        File.WriteAllText(targetPath, "bin/\nobj/\n");

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--adopt-existing"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("bin/\nobj/\n");
        await Assert
            .That(state.RequireValue().LockFile.Packs.Single().ManagedFiles)
            .Count()
            .IsEqualTo(1);
    }

    [Test]
    public async Task Install_WhenExistingTargetDiffersFromPackContent_PreservesProjectState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        var targetPath = Path.Combine(workspace.Path, ".gitignore");
        const string userContent = "# user owned\n";
        File.WriteAllText(targetPath, userContent);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--adopt-existing"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo(userContent);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Install_WhenPackAlreadyInstalled_LeavesManifestUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);
        var manifestPath = GetManifestPath(workspace.Path);
        var installedManifest = File.ReadAllText(manifestPath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(manifestPath)).IsEqualTo(installedManifest);
    }

    [Test]
    public async Task Install_WhenRequestedVersionUnavailable_SuggestsLatestVersion()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreateVersionedPackSource(workspace.Path, "version one", "version two");
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore@3.0.0"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(ansiConsole.Output)
            .Contains("Pack 'dotnet-gitignore' is unavailable at requested version '3.0.0'.");
        await Assert.That(ansiConsole.Output).Contains("Did");
        await Assert.That(ansiConsole.Output).Contains("you mean latest version '2.0.0'?");
    }

    [Test]
    public async Task Install_WhenDestinationAndAdoptionOptionsProvided_AcceptsOptions()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--destination", "docs/guidance", "--adopt-existing"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task Install_WhenDestinationProvided_RelocatesAndPersistsDirectPackState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--destination", "docs/guidance"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "guidance", ".gitignore")))
            .IsTrue();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        var projectState = state.RequireValue();
        await Assert
            .That(projectState.Configuration.Packs.Single().Destination)
            .IsEqualTo("docs/guidance");
        var resolvedPack = projectState.LockFile.Packs.Single();
        await Assert.That(resolvedPack.Destination).IsEqualTo("docs/guidance");
        await Assert
            .That(resolvedPack.ManagedFiles.Single().TargetPath)
            .IsEqualTo("docs/guidance/.gitignore");
    }

    [Test]
    public async Task Install_WhenDestinationUsesWindowsSeparators_PersistsCanonicalPaths()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--destination", "docs\\guidance"],
            workspace.Path
        );
        var configuration = File.ReadAllText(
            Path.Combine(workspace.Path, ProjectStateStore.ConfigurationFileName)
        );
        var lockFile = File.ReadAllText(
            Path.Combine(workspace.Path, ProjectStateStore.LockFileName)
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "guidance", ".gitignore")))
            .IsTrue();
        await Assert.That(configuration).DoesNotContain("\\");
        await Assert.That(lockFile).DoesNotContain("\\");
    }

    [Test]
    public async Task Install_WhenCompositePackHasDestination_PreservesDependencyTargets()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateCompositePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "foundation", "--destination", "docs/guidance"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "guidance", "foundation.txt")))
            .IsTrue();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "shared.txt"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "foundation.txt"))).IsFalse();
    }

    [Test]
    public async Task Install_WhenCompositeSetsTransientParameter_RendersFixedValue()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = Path.Combine(workspace.Path, "source");
        CreatePack(
            sourcePath,
            "shared",
            "id: shared\nversion: 1.0.0\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: shared.txt\n    template: true\n",
            "{{ companyName }}"
        );
        CreatePack(
            sourcePath,
            "foundation",
            "id: foundation\nversion: 1.0.0\npacks:\n  - id: shared\n    version: 1.0.0\n    parameters:\n      companyName: Lunaris\n",
            "unused"
        );
        await ConfigureSourceAsync(workspace, "source");

        var exitCode = await workspace.Application.RunAsync(
            ["install", "foundation"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "shared.txt")))
            .IsEqualTo("Lunaris");
    }

    [Test]
    public async Task Install_WhenAnyAdoptionTargetDiffers_PreservesEveryTargetAndState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateMultiTargetPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        var firstTargetPath = Path.Combine(workspace.Path, "first.txt");
        var secondTargetPath = Path.Combine(workspace.Path, "second.txt");
        File.WriteAllText(firstTargetPath, "first");
        File.WriteAllText(secondTargetPath, "user content");
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "multi-target", "--adopt-existing"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(firstTargetPath)).IsEqualTo("first");
        await Assert.That(File.ReadAllText(secondTargetPath)).IsEqualTo("user content");
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "third.txt"))).IsFalse();
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Install_WhenDestinationIsAbsolute_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--destination", workspace.Path],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Install_WhenDestinationEscapesProject_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--destination", "../outside"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Install_WhenDestinationEmpty_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--destination", string.Empty],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Install_WhenManifestWriteFails_RollsBackTarget()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await CreatePackLifecycleService(
                workspace,
                new FailingProjectStateStore(workspace.StateStore)
            )
            .InstallAsync(
                workspace.Path,
                new PackInstallationRequest(
                    new PackReference("dotnet-gitignore", null),
                    null,
                    false
                )
            );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
    }

    [Test]
    public async Task Uninstall_WhenTargetMissing_RemovesPackState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);
        File.Delete(Path.Combine(workspace.Path, ".gitignore"));
        var exitCode = await workspace.Application.RunAsync(
            ["uninstall", "dotnet-gitignore"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(state.RequireValue().Configuration.Packs).IsEmpty();
        await Assert.That(state.RequireValue().LockFile.Packs).IsEmpty();
    }

    [Test]
    public async Task Uninstall_WhenTargetUnmodified_RemovesRootAndResolvedState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["uninstall", "dotnet-gitignore"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        var projectState = state.RequireValue();
        await Assert.That(projectState.Configuration.Packs).IsEmpty();
        await Assert.That(projectState.LockFile.Packs).IsEmpty();
    }

    [Test]
    public async Task Uninstall_WhenSharedSectionMergeTarget_RemovesOnlyUninstalledSection()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedSectionMergePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore@1.0.0"], workspace.Path);
        await workspace.Application.RunAsync(
            ["install", "gitignore-general@1.0.0"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["uninstall", "dotnet-gitignore"],
            workspace.Path
        );
        var targetContents = File.ReadAllText(Path.Combine(workspace.Path, ".gitignore"));
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(targetContents).DoesNotContain("# dotnet:start");
        await Assert.That(targetContents).Contains("# general:start\n*.temporary\n# general:end");
        await Assert
            .That(state.RequireValue().Configuration.Packs.Select(pack => pack.Id))
            .IsEquivalentTo(["gitignore-general"]);
    }

    [Test]
    public async Task Uninstall_WhenLinesOrJsonMergeTarget_RetainsTarget()
    {
        foreach (var method in new[] { "lines", "json" })
        {
            using var workspace = new TestWorkspace();
            var sourcePath = CreatePackSource(
                workspace.Path,
                $"id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n    strategy:\n      type: merge\n      method: {method}\n"
            );
            await ConfigureSourceAsync(workspace, sourcePath);
            await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);

            var exitCode = await workspace.Application.RunAsync(
                ["uninstall", "dotnet-gitignore"],
                workspace.Path
            );
            var state = await workspace.StateStore.LoadAsync(workspace.Path);

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsTrue();
            await Assert.That(state.RequireValue().Configuration.Packs).IsEmpty();
        }
    }

    [Test]
    public async Task Uninstall_WhenPackInstalledAtDestination_RemovesEffectiveTarget()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        var destinationPath = Path.Combine(workspace.Path, "docs", "guidance", ".gitignore");
        await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--destination", "docs/guidance"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["uninstall", "dotnet-gitignore"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(destinationPath)).IsFalse();
        await Assert.That(state.RequireValue().Configuration.Packs).IsEmpty();
        await Assert.That(state.RequireValue().LockFile.Packs).IsEmpty();
    }

    [Test]
    public async Task Install_WhenDirectoryRemappingSpecified_WritesRemappedTargetAndLockIdentity()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: docs/adr/template.md\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            [
                "install",
                "dotnet-gitignore",
                "--remap-directory",
                "docs/adr=docs/internal/decisions",
            ],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        var managedFile = state.LockFile.Packs.Single().ManagedFiles.Single();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(
                File.Exists(
                    Path.Combine(workspace.Path, "docs", "internal", "decisions", "template.md")
                )
            )
            .IsTrue();
        await Assert.That(managedFile.DeclaredTargetPath).IsEqualTo("docs/adr/template.md");
        await Assert.That(managedFile.TargetPath).IsEqualTo("docs/internal/decisions/template.md");
    }

    [Test]
    public async Task Install_WhenSaveRemapSpecified_PersistsProvidedMappings()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(
            workspace.Path,
            "id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: docs/development/template.md\n"
        );
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            [
                "install",
                "dotnet-gitignore",
                "--remap-directory",
                "docs/development=docs/04-development",
                "--remap-file",
                "docs/development/template.md=docs/special/template.md",
                "--save-remap",
            ],
            workspace.Path
        );
        var remapping = (await workspace.StateStore.LoadAsync(workspace.Path))
            .RequireValue()
            .Configuration.Remap!;

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(remapping.Directories["docs/development"])
            .IsEqualTo("docs/04-development");
        await Assert
            .That(remapping.Files["docs/development/template.md"])
            .IsEqualTo("docs/special/template.md");
    }

    [Test]
    public async Task Install_WhenIgnoredFileRemapSaved_OmitsFileAndLockAndPersistsRemap()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--remap-file", ".gitignore=@ignore", "--save-remap"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        await Assert.That(state.LockFile.Packs.Single().ManagedFiles).IsEmpty();
        await Assert
            .That(state.Configuration.Remap!.Files[".gitignore"])
            .IsEqualTo(ManagedFileTargetRemapping.IgnoreTarget);
    }

    [Test]
    public async Task Uninstall_WhenTargetInstalledWithFileRemapping_RemovesRecordedEffectiveTarget()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        var remappedTarget = Path.Combine(workspace.Path, "docs", "managed", ".gitignore");

        await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore", "--remap-file", ".gitignore=docs/managed/.gitignore"],
            workspace.Path
        );
        var installedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        var exitCode = await workspace.Application.RunAsync(
            ["uninstall", "dotnet-gitignore"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert
            .That(installedState.LockFile.Packs.Single().ManagedFiles.Single().TargetPath)
            .IsEqualTo("docs/managed/.gitignore");
        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(remappedTarget)).IsFalse();
        await Assert.That(state.RequireValue().Configuration.Packs).IsEmpty();
        await Assert.That(state.RequireValue().LockFile.Packs).IsEmpty();
    }

    [Test]
    public async Task MoveManagedFile_WhenSourceExists_RelocatesFileAndUpdatesLockTarget()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["mv", ".gitignore", "docs/managed/.gitignore"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(0).Because(ansiConsole.Output);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "docs", "managed", ".gitignore")))
            .IsEqualTo("bin/\nobj/\n");
        await Assert
            .That(state.LockFile.Packs.Single().ManagedFiles.Single().TargetPath)
            .IsEqualTo("docs/managed/.gitignore");
    }

    [Test]
    public async Task MoveManagedFile_WhenSourceOwnedByLink_UpdatesLinkLockTarget()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        state.Configuration.Sources =
        [
            new ProjectConfiguration.LocalSource { Name = "local", Path = "source" },
        ];
        state.Configuration.Links["agents"] = new ProjectConfiguration.Link
        {
            Includes = ["expert.agent.md"],
            Source = "local",
        };
        state.LockFile.Links["agents"] = new ProjectLockFile.ResolvedLink
        {
            DefinitionSha256 = new string('A', 64),
            Files =
            [
                new ProjectLockFile.LinkFile
                {
                    DeclaredTargetPath = ".github/agents/expert.agent.md",
                    Sha256 = new string('B', 64),
                    SourcePath = "expert.agent.md",
                    TargetPath = ".github/agents/expert.agent.md",
                },
            ],
            SourceIdentity = ConfiguredSourceIdentity.CreateLocal("source"),
            SourceName = "local",
        };
        await workspace.StateStore.SaveAsync(workspace.Path, state);
        var sourceFile = Path.Combine(workspace.Path, ".github", "agents", "expert.agent.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, "expert");

        var exitCode = await CreatePackLifecycleService(workspace)
            .MoveManagedFileAsync(
                workspace.Path,
                ".github/agents/expert.agent.md",
                "docs/agents/expert.agent.md"
            );
        var updatedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(sourceFile)).IsFalse();
        await Assert
            .That(updatedState.LockFile.Links["agents"].Files.Single().TargetPath)
            .IsEqualTo("docs/agents/expert.agent.md");
    }

    [Test]
    public async Task MoveManagedFile_WhenPathsUseCurrentDirectoryAndWindowsSeparators_RelocatesFile()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["mv", ".\\.gitignore", ".\\docs\\managed\\.gitignore"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        await Assert
            .That(state.LockFile.Packs.Single().ManagedFiles.Single().TargetPath)
            .IsEqualTo("docs/managed/.gitignore");
    }

    [Test]
    public async Task MoveManagedFile_WhenSourceIsDirectory_RelocatesAllOwnedDescendants()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateSelectorPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "selectors"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["mv", "directory-output", "docs/04-development"],
            workspace.Path
        );
        var targets = (await workspace.StateStore.LoadAsync(workspace.Path))
            .RequireValue()
            .LockFile.Packs.Single()
            .ManagedFiles.Where(file =>
                file.DeclaredTargetPath is { } declaredTarget
                && declaredTarget.StartsWith("directory-output/", StringComparison.Ordinal)
            )
            .Select(file => file.TargetPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(targets)
            .IsEquivalentTo([
                "docs/04-development/nested/child.txt",
                "docs/04-development/root.txt",
            ]);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "directory-output", "root.txt")))
            .IsFalse();
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "04-development", "root.txt")))
            .IsTrue();
    }

    [Test]
    public async Task MoveManagedFile_WhenSaveRemapSpecified_PersistsDirectoryMapping()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateSelectorPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "selectors"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["mv", "directory-output", "docs/04-development", "--save-remap"],
            workspace.Path
        );
        var remapping = (await workspace.StateStore.LoadAsync(workspace.Path))
            .RequireValue()
            .Configuration.Remap!;

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(remapping.Directories["directory-output"])
            .IsEqualTo("docs/04-development");
    }

    [Test]
    public async Task MoveManagedFile_WhenFileSaveRemapSpecified_PersistsDeclaredFileMapping()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["mv", ".gitignore", "docs/managed/.gitignore", "--save-remap"],
            workspace.Path
        );
        var remapping = (await workspace.StateStore.LoadAsync(workspace.Path))
            .RequireValue()
            .Configuration.Remap!;

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(remapping.Files[".gitignore"]).IsEqualTo("docs/managed/.gitignore");
    }

    [Test]
    public async Task MoveManagedFile_WhenSourceAlreadyMoved_RebindsLockTarget()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);
        var originalFile = Path.Combine(workspace.Path, ".gitignore");
        var relocatedFile = Path.Combine(workspace.Path, "docs", "managed", ".gitignore");
        Directory.CreateDirectory(Path.GetDirectoryName(relocatedFile)!);
        File.Move(originalFile, relocatedFile);

        var exitCode = await workspace.Application.RunAsync(
            ["mv", ".gitignore", "docs/managed/.gitignore"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(relocatedFile)).IsEqualTo("bin/\nobj/\n");
        await Assert
            .That(state.LockFile.Packs.Single().ManagedFiles.Single().TargetPath)
            .IsEqualTo("docs/managed/.gitignore");
    }

    [Test]
    public async Task MoveManagedFile_WhenBothFilesExist_RefusesToChangeOwnership()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);
        var relocatedFile = Path.Combine(workspace.Path, "docs", "managed", ".gitignore");
        Directory.CreateDirectory(Path.GetDirectoryName(relocatedFile)!);
        File.WriteAllText(relocatedFile, "existing target");

        var exitCode = await workspace.Application.RunAsync(
            ["mv", ".gitignore", "docs/managed/.gitignore"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".gitignore")))
            .IsEqualTo("bin/\nobj/\n");
        await Assert.That(File.ReadAllText(relocatedFile)).IsEqualTo("existing target");
        await Assert
            .That(state.LockFile.Packs.Single().ManagedFiles.Single().TargetPath)
            .IsEqualTo(".gitignore");
    }

    [Test]
    public async Task MoveManagedFile_WhenStateSaveFails_RestoresSourceAndLockState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);
        var lockFilePath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var initialLockFile = File.ReadAllText(lockFilePath);

        var exitCode = await CreatePackLifecycleService(
                workspace,
                new FailingProjectStateStore(workspace.StateStore)
            )
            .MoveManagedFileAsync(workspace.Path, ".gitignore", "docs/managed/.gitignore");

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".gitignore")))
            .IsEqualTo("bin/\nobj/\n");
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "managed", ".gitignore")))
            .IsFalse();
        await Assert.That(File.ReadAllText(lockFilePath)).IsEqualTo(initialLockFile);
    }

    [Test]
    public async Task MoveManagedFile_WhenDirectoryStateSaveFails_RestoresEverySourceFile()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateSelectorPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "selectors"], workspace.Path);
        var lockFilePath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var initialLockFile = File.ReadAllText(lockFilePath);

        var exitCode = await CreatePackLifecycleService(
                workspace,
                new FailingProjectStateStore(workspace.StateStore)
            )
            .MoveManagedFileAsync(
                workspace.Path,
                "directory-output",
                "docs/04-development",
                saveRemapping: true
            );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "directory-output", "root.txt")))
            .IsTrue()
            .Because(
                string.Join(
                    ", ",
                    Directory.EnumerateFiles(workspace.Path, "*", SearchOption.AllDirectories)
                )
            );
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "04-development", "root.txt")))
            .IsFalse();
        await Assert.That(File.ReadAllText(lockFilePath)).IsEqualTo(initialLockFile);
    }

    [Test]
    public async Task MoveManagedFile_WhenDirectoryBatchMixesMoveAndRebind_RefusesAllChanges()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateSelectorPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "selectors"], workspace.Path);
        var sourceRoot = Path.Combine(workspace.Path, "directory-output");
        var targetRoot = Path.Combine(workspace.Path, "docs", "04-development");
        var manuallyMovedSource = Path.Combine(sourceRoot, "root.txt");
        var manuallyMovedTarget = Path.Combine(targetRoot, "root.txt");
        Directory.CreateDirectory(targetRoot);
        File.Move(manuallyMovedSource, manuallyMovedTarget);

        var exitCode = await CreatePackLifecycleService(workspace)
            .MoveManagedFileAsync(workspace.Path, "directory-output", "docs/04-development");
        var targets = (await workspace.StateStore.LoadAsync(workspace.Path))
            .RequireValue()
            .LockFile.Packs.Single()
            .ManagedFiles.Select(file => file.TargetPath)
            .ToList();

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.Exists(manuallyMovedTarget)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(sourceRoot, "nested", "child.txt"))).IsTrue();
        await Assert.That(targets).Contains("directory-output/root.txt");
        await Assert.That(targets).Contains("directory-output/nested/child.txt");
    }

    [Test]
    public async Task Uninstall_WhenTargetModified_PreservesProjectState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore"], workspace.Path);
        var targetPath = Path.Combine(workspace.Path, ".gitignore");
        const string modifiedContent = "# user change\n";
        File.WriteAllText(targetPath, modifiedContent);
        var configurationPath = GetManifestPath(workspace.Path);
        var lockFilePath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var initialConfiguration = File.ReadAllText(configurationPath);
        var initialLockFile = File.ReadAllText(lockFilePath);

        var exitCode = await workspace.Application.RunAsync(
            ["uninstall", "dotnet-gitignore"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo(modifiedContent);
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
        await Assert.That(File.ReadAllText(lockFilePath)).IsEqualTo(initialLockFile);
    }

    [Test]
    public async Task Install_WhenSuccessful_RecordsSha256Digest()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreatePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(state.RequireValue().LockFile.Packs[0].ManagedFiles[0].Sha256)
            .Matches("^[A-F0-9]{64}$");
        await Assert.That(state.RequireValue().LockFile.SchemaVersion).IsEqualTo(1);
        await Assert
            .That(state.RequireValue().LockFile.Packs[0].ManagedFiles[0].DeclaredTargetPath)
            .IsEqualTo(".gitignore");
    }

    [Test]
    public async Task Scenario_InstallAndUpdateSharedMergeTargets_PreservesBothSectionsAndOwners()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedSectionMergePackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var firstInstallExitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore@1.0.0"],
            workspace.Path
        );
        await Assert.That(firstInstallExitCode).IsEqualTo(0);
        var secondInstallDryRun = await CreatePackLifecycleService(workspace)
            .DryRunInstallAsync(
                workspace.Path,
                new PackInstallationRequest(
                    new PackReference("gitignore-general", "1.0.0"),
                    null,
                    false
                )
            );
        await Assert.That(secondInstallDryRun.Error).IsNull();
        var secondInstallExitCode = await workspace.Application.RunAsync(
            ["install", "gitignore-general@1.0.0"],
            workspace.Path
        );
        var targetPath = Path.Combine(workspace.Path, ".gitignore");
        var installedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(secondInstallExitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(targetPath))
            .Contains("# dotnet:start\nbin/\n# dotnet:end");
        await Assert
            .That(File.ReadAllText(targetPath))
            .Contains("# general:start\n*.temporary\n# general:end");
        await Assert.That(installedState.Configuration.Packs).Count().IsEqualTo(2);
        await Assert.That(installedState.LockFile.Packs).Count().IsEqualTo(2);

        var updateExitCode = await CreatePackLifecycleService(workspace)
            .UpdateAsync(
                workspace.Path,
                [
                    new ProjectConfiguration.RequestedPack
                    {
                        Id = "dotnet-gitignore",
                        Version = "2.0.0",
                    },
                    new ProjectConfiguration.RequestedPack
                    {
                        Id = "gitignore-general",
                        Version = "2.0.0",
                    },
                ],
                new PackInstallationRequest(
                    new PackReference("dotnet-gitignore", "2.0.0"),
                    null,
                    false
                )
            );
        var updatedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        var finalContents = File.ReadAllText(targetPath);
        var finalDigest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(finalContents))
        );

        await Assert.That(updateExitCode).IsEqualTo(0);
        await Assert.That(finalContents).Contains("# dotnet:start\nbin-v2/\n# dotnet:end");
        await Assert.That(finalContents).Contains("# general:start\n*.temporary-v2\n# general:end");
        await Assert.That(updatedState.Configuration.Packs).Count().IsEqualTo(2);
        await Assert.That(updatedState.LockFile.Packs).Count().IsEqualTo(2);
        await Assert
            .That(
                updatedState
                    .LockFile.Packs.SelectMany(pack => pack.ManagedFiles)
                    .Select(managedFile => managedFile.Sha256)
            )
            .IsEquivalentTo([finalDigest, finalDigest]);
    }

    [Test]
    public async Task Update_WhenTargetOwnedByPriorVersion_RefreshesRootsAndCompleteLockGraph()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "version one", "version two");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore@1.0.0"], workspace.Path);

        var exitCode = await CreatePackLifecycleService(workspace)
            .UpdateAsync(
                workspace.Path,
                [
                    new ProjectConfiguration.RequestedPack
                    {
                        Id = "dotnet-gitignore",
                        Version = "2.0.0",
                    },
                ],
                new PackInstallationRequest(
                    new PackReference("dotnet-gitignore", "2.0.0"),
                    null,
                    false
                )
            );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".gitignore")))
            .IsEqualTo("version two");
        var projectState = state.RequireValue();
        await Assert.That(projectState.Configuration.Packs.Single().Version).IsEqualTo("2.0.0");
        await Assert.That(projectState.LockFile.Packs).Count().IsEqualTo(1);
        var resolvedPack = projectState.LockFile.Packs.Single();
        await Assert.That(resolvedPack.Version).IsEqualTo("2.0.0");
        await Assert
            .That(resolvedPack.ManagedFiles.Single().Sha256)
            .IsEqualTo(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("version two"))));
    }

    [Test]
    public async Task UpdateAndUninstall_WhenScriptsDenied_WarnAndCompleteWithoutScripts()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = false;
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourceDirectory = Path.Combine(workspace.Path, "source");
        CreatePack(
            sourceDirectory,
            "dotnet-gitignore-v1",
            "id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: .gitignore\n",
            "version one"
        );
        CreatePack(
            sourceDirectory,
            "dotnet-gitignore-v2",
            $"id: dotnet-gitignore\nversion: 2.0.0\nhooks:\n  preUpdate:\n    - type: script\n      command: {ShellExecutable}\n      arguments: [{ShellArgument}, 'echo pre-update > lifecycle.txt']\n  postUpdate:\n    - type: script\n      command: {ShellExecutable}\n      arguments: [{ShellArgument}, 'echo post-update >> lifecycle.txt']\n  preUninstall:\n    - type: script\n      command: {ShellExecutable}\n      arguments: [{ShellArgument}, 'echo pre-uninstall >> lifecycle.txt']\n  postUninstall:\n    - type: script\n      command: {ShellExecutable}\n      arguments: [{ShellArgument}, 'echo post-uninstall >> lifecycle.txt']\nmanagedFiles:\n  - source: templates/content.txt\n    target: .gitignore\n",
            "version two"
        );
        await ConfigureSourceAsync(workspace, "source");
        await workspace.Application.RunAsync(["install", "dotnet-gitignore@1.0.0"], workspace.Path);
        await workspace.Application.RunAsync(
            ["trust", "scripts", "deny", "--project"],
            workspace.Path
        );

        var updateExitCode = await workspace.Application.RunAsync(
            ["update", "dotnet-gitignore", "--scripts", "run"],
            workspace.Path
        );
        var uninstallExitCode = await workspace.Application.RunAsync(
            ["uninstall", "dotnet-gitignore", "--scripts", "run"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(updateExitCode).IsEqualTo(0);
        await Assert.That(uninstallExitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("event preUpdate");
        await Assert.That(ansiConsole.Output).Contains("event postUpdate");
        await Assert.That(ansiConsole.Output).Contains("event preUninstall");
        await Assert.That(ansiConsole.Output).Contains("event postUninstall");
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "lifecycle.txt"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        await Assert.That(state.RequireValue().Configuration.Packs).IsEmpty();
        await Assert.That(state.RequireValue().LockFile.Packs).IsEmpty();
    }

    [Test]
    public async Task Update_WhenGlobalRemappingChanges_RetainsLockedEffectiveTarget()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "version one", "version two");
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        initialState.Configuration.Remap = new ProjectConfiguration.Remapping
        {
            Files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".gitignore"] = "docs/initial/.gitignore",
            },
        };
        await workspace.StateStore.SaveAsync(workspace.Path, initialState);
        await workspace.Application.RunAsync(["install", "dotnet-gitignore@1.0.0"], workspace.Path);

        var installedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        installedState.Configuration.Remap = new ProjectConfiguration.Remapping
        {
            Files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".gitignore"] = "docs/current/.gitignore",
            },
        };
        await workspace.StateStore.SaveAsync(workspace.Path, installedState);

        var exitCode = await CreatePackLifecycleService(workspace)
            .UpdateAsync(
                workspace.Path,
                [
                    new ProjectConfiguration.RequestedPack
                    {
                        Id = "dotnet-gitignore",
                        Version = "2.0.0",
                    },
                ],
                new PackInstallationRequest(
                    new PackReference("dotnet-gitignore", "2.0.0"),
                    null,
                    false
                )
            );
        var updatedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        var managedFile = updatedState.LockFile.Packs.Single().ManagedFiles.Single();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "docs", "initial", ".gitignore")))
            .IsEqualTo("version two");
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "current", ".gitignore")))
            .IsFalse();
        await Assert.That(managedFile.DeclaredTargetPath).IsEqualTo(".gitignore");
        await Assert.That(managedFile.TargetPath).IsEqualTo("docs/initial/.gitignore");
    }

    [Test]
    public async Task Update_WhenManagedTargetBecomesIgnored_PreservesFileAndDropsLockOwnership()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "version one", "version two");
        await ConfigureSourceAsync(workspace, sourcePath);
        var installExitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore@1.0.0"],
            workspace.Path
        );
        await Assert.That(installExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsTrue();
        var installedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        installedState.Configuration.Remap = new ProjectConfiguration.Remapping
        {
            Files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".gitignore"] = ManagedFileTargetRemapping.IgnoreTarget,
            },
        };
        await workspace.StateStore.SaveAsync(workspace.Path, installedState);

        var exitCode = await CreatePackLifecycleService(workspace)
            .UpdateAsync(
                workspace.Path,
                [
                    new ProjectConfiguration.RequestedPack
                    {
                        Id = "dotnet-gitignore",
                        Version = "2.0.0",
                    },
                ],
                new PackInstallationRequest(
                    new PackReference("dotnet-gitignore", "2.0.0"),
                    null,
                    false
                )
            );
        var updatedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".gitignore")))
            .IsEqualTo("version one");
        await Assert.That(updatedState.LockFile.Packs.Single().ManagedFiles).IsEmpty();
    }

    [Test]
    public async Task Update_WhenIgnoredManagedTargetBecomesActive_InstallsAndLocksLatestFile()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "version one", "version two");
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        initialState.Configuration.Remap = new ProjectConfiguration.Remapping
        {
            Files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".gitignore"] = ManagedFileTargetRemapping.IgnoreTarget,
            },
        };
        await workspace.StateStore.SaveAsync(workspace.Path, initialState);
        var installExitCode = await workspace.Application.RunAsync(
            ["install", "dotnet-gitignore@1.0.0"],
            workspace.Path
        );
        await Assert.That(installExitCode).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Path, ".gitignore"))).IsFalse();
        var ignoredState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        ignoredState.Configuration.Remap = null;
        await workspace.StateStore.SaveAsync(workspace.Path, ignoredState);

        var exitCode = await CreatePackLifecycleService(workspace)
            .UpdateAsync(
                workspace.Path,
                [
                    new ProjectConfiguration.RequestedPack
                    {
                        Id = "dotnet-gitignore",
                        Version = "2.0.0",
                    },
                ],
                new PackInstallationRequest(
                    new PackReference("dotnet-gitignore", "2.0.0"),
                    null,
                    false
                )
            );
        var updatedState = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, ".gitignore")))
            .IsEqualTo("version two");
        await Assert.That(updatedState.LockFile.Packs.Single().ManagedFiles).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Update_WhenStateSaveFails_RestoresTargetsAndProjectState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "version one", "version two");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(
            [
                "install",
                "dotnet-gitignore@1.0.0",
                "--remap-file",
                ".gitignore=docs/managed/.gitignore",
            ],
            workspace.Path
        );
        var configurationPath = GetManifestPath(workspace.Path);
        var lockFilePath = Path.Combine(workspace.Path, ProjectStateStore.LockFileName);
        var initialConfiguration = File.ReadAllText(configurationPath);
        var initialLockFile = File.ReadAllText(lockFilePath);

        var exitCode = await CreatePackLifecycleService(
                workspace,
                new FailingProjectStateStore(workspace.StateStore)
            )
            .UpdateAsync(
                workspace.Path,
                [
                    new ProjectConfiguration.RequestedPack
                    {
                        Id = "dotnet-gitignore",
                        Version = "2.0.0",
                    },
                ],
                new PackInstallationRequest(
                    new PackReference("dotnet-gitignore", "2.0.0"),
                    null,
                    false
                )
            );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "docs", "managed", ".gitignore")))
            .IsEqualTo("version one");
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(initialConfiguration);
        await Assert.That(File.ReadAllText(lockFilePath)).IsEqualTo(initialLockFile);
    }

    [Test]
    public async Task Install_WhenDirectoryAndGlobSelectorsDeclared_CopiesMatchingFilesRecursively()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateSelectorPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "selectors"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "directory-output", "root.txt")))
            .IsEqualTo("directory root");
        await Assert
            .That(
                File.ReadAllText(
                    Path.Combine(workspace.Path, "directory-output", "nested", "child.txt")
                )
            )
            .IsEqualTo("directory child");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "glob-output", "root.json")))
            .IsEqualTo("glob root");
        await Assert
            .That(
                File.ReadAllText(
                    Path.Combine(workspace.Path, "glob-output", "nested", "child.json")
                )
            )
            .IsEqualTo("glob child");
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "glob-output", "nested", "ignored.txt")))
            .IsFalse();
    }

    [Test]
    public async Task Install_WhenDirectorySelectorRemappedByInvocation_WritesRemappedFiles()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateSelectorPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);

        var exitCode = await workspace.Application.RunAsync(
            ["install", "selectors", "--remap-directory", "directory-output=docs/04-development"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        var directoryTargets = state
            .LockFile.Packs.Single()
            .ManagedFiles.Where(file =>
                file.DeclaredTargetPath is { } declaredTargetPath
                && declaredTargetPath.StartsWith("directory-output/", StringComparison.Ordinal)
            )
            .Select(file => file.TargetPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "04-development", "root.txt")))
            .IsTrue();
        await Assert
            .That(directoryTargets)
            .IsEquivalentTo([
                "docs/04-development/nested/child.txt",
                "docs/04-development/root.txt",
            ]);
    }

    [Test]
    public async Task Install_WhenDirectoryManagedFileHasConfiguredFileRemapping_WritesRemappedFile()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateSelectorPackSource(workspace.Path);
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(
            ["remap", "set", "file", "directory-output/root.txt", "docs/04-development/root.txt"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["install", "selectors"],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();
        var rootFile = state
            .LockFile.Packs.Single()
            .ManagedFiles.Single(file =>
                string.Equals(
                    file.DeclaredTargetPath,
                    "directory-output/root.txt",
                    StringComparison.Ordinal
                )
            );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, "docs", "04-development", "root.txt")))
            .IsTrue();
        await Assert.That(rootFile.TargetPath).IsEqualTo("docs/04-development/root.txt");
    }

    private static async Task ConfigureSourceAsync(TestWorkspace workspace, string sourcePath)
    {
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", sourcePath],
            workspace.Path
        );
    }

    private const string RequiredCompanyParameterManifest =
        "id: dotnet-gitignore\nversion: 1.0.0\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n    template: true\n";

    private static string CreatePackSource(
        string projectDirectory,
        string? manifest = null,
        string templateContents = "bin/\nobj/\n"
    )
    {
        var packDirectory = Path.Combine(projectDirectory, "source", "dotnet-gitignore");
        var templateDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templateDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            AddRequiredMetadata(
                manifest
                    ?? "id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/dotnet.gitignore\n    target: .gitignore\n"
            )
        );
        File.WriteAllText(Path.Combine(templateDirectory, "dotnet.gitignore"), templateContents);

        return "source";
    }

    private static string CreateVersionedPackSource(
        string projectDirectory,
        string firstContents,
        string secondContents
    )
    {
        var sourcePath = Path.Combine(projectDirectory, "source");
        CreatePack(
            sourcePath,
            "dotnet-gitignore-v1",
            "id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: .gitignore\n",
            firstContents
        );
        CreatePack(
            sourcePath,
            "dotnet-gitignore-v2",
            "id: dotnet-gitignore\nversion: 2.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: .gitignore\n",
            secondContents
        );

        return "source";
    }

    private static string CreateVersionedManagedPathTemplateSource(string projectDirectory)
    {
        var sourcePath = Path.Combine(projectDirectory, "source");
        CreateManagedPathTemplatePack(sourcePath, "v1", "1.0.0");
        CreateManagedPathTemplatePack(sourcePath, "v2", "2.0.0");
        return "source";
    }

    private static void CreateManagedPathTemplatePack(
        string sourcePath,
        string directoryName,
        string version
    )
    {
        var packDirectory = Path.Combine(sourcePath, $"dotnet-gitignore-{directoryName}");
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templatesDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            AddRequiredMetadata(
                $"id: dotnet-gitignore\nversion: {version}\nmanagedFiles:\n  - source: templates/index.md\n    target: docs/index.md\n    template: true\n  - source: templates/ref.md\n    target: docs/ref.md\n"
            )
        );
        File.WriteAllText(
            Path.Combine(templatesDirectory, "index.md"),
            $"{directoryName}:{{{{ files.path 'docs/ref.md' }}}}|{{{{ files.relative_path 'docs/ref.md' }}}}"
        );
        File.WriteAllText(Path.Combine(templatesDirectory, "ref.md"), "reference");
    }

    private static string GetPlannedContents(PackUpdatePlan plan, string targetPath)
    {
        var contents = plan
            .Actions.Single(action =>
                string.Equals(
                    action.TargetPathRelativeToProject,
                    targetPath,
                    StringComparison.Ordinal
                )
            )
            .ResultingContents;
        return contents is null
            ? throw new InvalidOperationException($"Target '{targetPath}' has no planned content.")
            : Encoding.UTF8.GetString(contents);
    }

    private static string CreateVersionedSectionMergePackSource(string projectDirectory)
    {
        var sourcePath = Path.Combine(projectDirectory, "source");
        CreatePack(
            sourcePath,
            "dotnet-gitignore-v1",
            "id: dotnet-gitignore\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: .gitignore\n    strategy:\n      type: merge\n      method: section\n",
            "# dotnet:start\nbin/\n# dotnet:end\n"
        );
        CreatePack(
            sourcePath,
            "dotnet-gitignore-v2",
            "id: dotnet-gitignore\nversion: 2.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: .gitignore\n    strategy:\n      type: merge\n      method: section\n",
            "# dotnet:start\nbin-v2/\n# dotnet:end\n"
        );
        CreatePack(
            sourcePath,
            "gitignore-general-v1",
            "id: gitignore-general\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: .gitignore\n    strategy:\n      type: merge\n      method: section\n",
            "# general:start\n*.temporary\n# general:end\n"
        );
        CreatePack(
            sourcePath,
            "gitignore-general-v2",
            "id: gitignore-general\nversion: 2.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: .gitignore\n    strategy:\n      type: merge\n      method: section\n",
            "# general:start\n*.temporary-v2\n# general:end\n"
        );

        return "source";
    }

    private static PackLifecycleService CreatePackLifecycleService(
        TestWorkspace workspace,
        IProjectStateStore? projectStateStore = null
    )
    {
        var packCatalog = new PackCatalog(workspace.FileSystem, TestConsole.Create());
        return new PackLifecycleService(
            workspace.FileSystem,
            new CompositePackGraphResolver(packCatalog),
            new PackInstallationPlanner(
                workspace.FileSystem,
                new PackTemplateRenderer(workspace.FileSystem),
                new ManagedFileConditionParser()
            ),
            new PackUpdatePlanner(workspace.FileSystem),
            new PackUpdateTransaction(workspace.FileSystem, TestConsole.Create()),
            projectStateStore ?? workspace.StateStore,
            TestConsole.Create()
        );
    }

    private static string CreateCompositePackSource(string projectDirectory)
    {
        var sourcePath = Path.Combine(projectDirectory, "source");
        CreatePack(
            sourcePath,
            "shared",
            "id: shared\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: shared.txt\n",
            "shared"
        );
        CreatePack(
            sourcePath,
            "foundation",
            "id: foundation\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: foundation.txt\npacks:\n  - id: shared\n    version: 1.0.0\n",
            "foundation"
        );

        return "source";
    }

    private static string CreateSharedTransientPackSource(string projectDirectory)
    {
        var sourcePath = Path.Combine(projectDirectory, "source");
        CreatePack(
            sourcePath,
            "shared",
            "id: shared\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: shared.txt\n",
            "shared"
        );
        CreatePack(
            sourcePath,
            "root-one",
            "id: root-one\nversion: 1.0.0\npacks:\n  - id: shared\n    version: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: root-one.txt\n",
            "root one"
        );
        CreatePack(
            sourcePath,
            "root-two",
            "id: root-two\nversion: 1.0.0\npacks:\n  - id: shared\n    version: 1.0.0\nmanagedFiles:\n  - source: templates/content.txt\n    target: root-two.txt\n",
            "root two"
        );

        return "source";
    }

    private static string CreateMultiTargetPackSource(string projectDirectory)
    {
        var packDirectory = Path.Combine(projectDirectory, "source", "multi-target");
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templatesDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            "id: multi-target\nversion: 1.0.0\nmanagedFiles:\n  - source: templates/first.txt\n    target: first.txt\n  - source: templates/second.txt\n    target: second.txt\n  - source: templates/third.txt\n    target: third.txt\n"
        );
        File.WriteAllText(Path.Combine(templatesDirectory, "first.txt"), "first");
        File.WriteAllText(Path.Combine(templatesDirectory, "second.txt"), "second");
        File.WriteAllText(Path.Combine(templatesDirectory, "third.txt"), "third");

        return "source";
    }

    private static void CreatePack(
        string sourcePath,
        string packId,
        string manifest,
        string templateContents
    )
    {
        var packDirectory = Path.Combine(sourcePath, packId);
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templatesDirectory);
        File.WriteAllText(Path.Combine(packDirectory, "pack.yml"), AddRequiredMetadata(manifest));
        File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), templateContents);
    }

    private static string CreateSelectorPackSource(string projectDirectory)
    {
        var packDirectory = Path.Combine(projectDirectory, "source", "selectors");
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(Path.Combine(templatesDirectory, "directory", "nested"));
        Directory.CreateDirectory(Path.Combine(templatesDirectory, "glob", "nested"));
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            "id: selectors\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - directory: templates/directory\n    target: directory-output\n  - glob: templates/glob/**/*.json\n    target: glob-output\n"
        );
        File.WriteAllText(
            Path.Combine(templatesDirectory, "directory", "root.txt"),
            "directory root"
        );
        File.WriteAllText(
            Path.Combine(templatesDirectory, "directory", "nested", "child.txt"),
            "directory child"
        );
        File.WriteAllText(Path.Combine(templatesDirectory, "glob", "root.json"), "glob root");
        File.WriteAllText(
            Path.Combine(templatesDirectory, "glob", "nested", "child.json"),
            "glob child"
        );
        File.WriteAllText(
            Path.Combine(templatesDirectory, "glob", "nested", "ignored.txt"),
            "ignored"
        );

        return "source";
    }

    private static string GetManifestPath(string projectDirectory) =>
        Path.Combine(projectDirectory, ProjectManifestStore.FileName);

    private static string ShellExecutable => OperatingSystem.IsWindows() ? "cmd" : "/bin/sh";

    private static string ShellArgument => OperatingSystem.IsWindows() ? "/c" : "-c";

    private static string FailureCommand => OperatingSystem.IsWindows() ? "exit /b 7" : "exit 7";

    private static string DeleteManifestCommand =>
        OperatingSystem.IsWindows() ? "del lunapack.yml" : "rm lunapack.yml";

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

    private static async Task<string> ReadStateAsync(string projectDirectory) =>
        string.Concat(
            await File.ReadAllTextAsync(GetManifestPath(projectDirectory)),
            await File.ReadAllTextAsync(
                Path.Combine(projectDirectory, ProjectStateStore.LockFileName)
            )
        );
}
