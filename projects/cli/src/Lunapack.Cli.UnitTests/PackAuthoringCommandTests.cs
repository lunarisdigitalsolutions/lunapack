using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests;

public sealed class PackAuthoringCommandTests
{
    [Test]
    public async Task Pack_WhenManifestMissing_RendersInitializationNextStep()
    {
        var console = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: console);

        var exitCode = await workspace.Application.RunAsync(["pack"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("luna pack init");
    }

    [Test]
    public async Task Pack_WhenManifestExists_RendersAuthoringNextSteps()
    {
        var console = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: console);
        await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example", "--author", "Example Author", "--license", "MIT"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["pack"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("luna pack show");
        await Assert.That(console.Output).Contains("luna pack validate");
    }

    [Test]
    public async Task Init_WhenIdMissingAndConsoleNotInteractive_DoesNotCreateManifest()
    {
        using var workspace = new TestWorkspace();

        var exitCode = await workspace.Application.RunAsync(["pack", "init"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, PackManifestStore.FileName)))
            .IsFalse();
    }

    [Test]
    [Arguments("--author", "Example Author")]
    [Arguments("--license", "MIT")]
    public async Task Init_WhenRequiredMetadataMissingAndConsoleNotInteractive_DoesNotCreateManifest(
        string missingOption,
        string value
    )
    {
        using var workspace = new TestWorkspace();
        var providedOption = string.Equals(missingOption, "--author", StringComparison.Ordinal)
            ? "--license"
            : "--author";

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example", providedOption, value],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, PackManifestStore.FileName)))
            .IsFalse();
    }

    [Test]
    public async Task Init_WhenRequiredOptionsProvided_CreatesRequiredMetadataManifest()
    {
        using var workspace = new TestWorkspace();

        var exitCode = await workspace.Application.RunAsync(
            [
                "pack",
                "init",
                "--id",
                "example",
                "--version",
                "1.2.3",
                "--author",
                "Example Author",
                "--license",
                "MIT",
            ],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        var contents = File.ReadAllText(Path.Combine(workspace.Path, PackManifestStore.FileName));
        await Assert.That(contents).Contains("author: Example Author");
        await Assert.That(contents).Contains("id: example");
        await Assert.That(contents).Contains("license: MIT");
        await Assert.That(contents).Contains("version: 1.2.3");
        await Assert.That(ManifestModelValidator.Validate(await LoadAsync(workspace))).IsEmpty();
    }

    [Test]
    public async Task Init_WhenLicenseIsPromptedAndLeftEmpty_UsesMit()
    {
        var console = new SpectreTestConsole();
        Spectre.Console.Testing.TestConsoleExtensions.Interactive(console);
        console.Input.PushTextWithEnter(string.Empty);
        using var workspace = new TestWorkspace(ansiConsole: console);

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example", "--author", "Example Author"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, PackManifestStore.FileName)))
            .Contains("license: MIT");
    }

    [Test]
    public async Task Init_WhenIdIsNotKebabCase_DoesNotCreateManifest()
    {
        using var workspace = new TestWorkspace();

        var exitCode = await workspace.Application.RunAsync(
            [
                "pack",
                "init",
                "--id",
                "example_pack",
                "--author",
                "Example Author",
                "--license",
                "MIT",
            ],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, PackManifestStore.FileName)))
            .IsFalse();
    }

    [Test]
    public async Task Init_WhenPromptedIdIsInvalid_RendersErrorAndPromptsAgain()
    {
        var console = new SpectreTestConsole();
        Spectre.Console.Testing.TestConsoleExtensions.Interactive(console);
        console.Input.PushTextWithEnter("example_pack");
        console.Input.PushTextWithEnter("example-pack");
        console.Input.PushTextWithEnter("Example Author");
        console.Input.PushTextWithEnter(string.Empty);
        using var workspace = new TestWorkspace(ansiConsole: console);

        var exitCode = await workspace.Application.RunAsync(["pack", "init"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(console.Output)
            .Contains("Pack ID 'example_pack' must use hyphen-separated alphanumeric segments.");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, PackManifestStore.FileName)))
            .Contains("id: example-pack");
    }

    [Test]
    public async Task Init_WhenManifestCreated_RendersAuthoringNextStep()
    {
        var console = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: console);

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example", "--author", "Example Author", "--license", "MIT"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("luna pack add file <path>");
    }

    [Test]
    public async Task Init_WhenManifestExists_PreservesOriginalBytes()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        const string original = "id: existing\nversion: 1.0.0\n";
        File.WriteAllText(path, original);

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "init", "--id", "replacement"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task AddManagedContent_WhenInputsValid_NormalizesAndPersistsSelectors()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();

        var fileExit = await workspace.Application.RunAsync(
            ["pack", "add", "file", @"docs\README.md"],
            workspace.Path
        );
        var directoryExit = await workspace.Application.RunAsync(
            ["pack", "add", "directory", @"templates\api"],
            workspace.Path
        );
        var globExit = await workspace.Application.RunAsync(
            ["pack", "add", "glob", @"guides\**\*.md"],
            workspace.Path
        );
        var manifest = await LoadAsync(workspace);

        await Assert
            .That(new[] { fileExit, directoryExit, globExit }.All(code => code == 0))
            .IsTrue();
        await Assert
            .That(manifest.ManagedFiles.Select(file => file.Path))
            .Contains("docs/README.md");
        await Assert
            .That(manifest.ManagedFiles.Select(file => file.Directory))
            .Contains("templates/api");
        await Assert
            .That(manifest.ManagedFiles.Select(file => file.Glob))
            .Contains("guides/**/*.md");
        await Assert
            .That(manifest.ManagedFiles.Single(file => file.Glob is not null).Target)
            .IsEqualTo("guides");
    }

    [Test]
    public async Task AddFile_WhenPathEscapes_PreservesManifest()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var original = File.ReadAllText(path);

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "add", "file", "../secret.txt"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task AddGitSource_WhenInputsValid_PersistsCanonicalDeclaration()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();

        var exitCode = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "source",
                "git",
                "upstream",
                "https://github.com/Example/Standards.git",
                "--ref",
                "main",
                "--path",
                @"docs\standards",
                "--description",
                "Shared standards",
            ],
            workspace.Path
        );
        var source = (await LoadAsync(workspace)).Sources["upstream"];

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.Ref).IsEqualTo("refs/heads/main");
        await Assert.That(source.Path).IsEqualTo("docs/standards");
        await Assert.That(source.Description).IsEqualTo("Shared standards");
    }

    [Test]
    public async Task AddGitHubSource_WhenInputsValid_PersistsGitDeclaration()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "add", "source", "github", "upstream", "Example/Standards", "--ref", "main"],
            workspace.Path
        );
        var source = (await LoadAsync(workspace)).Sources["upstream"];

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.Url).IsEqualTo("https://github.com/Example/Standards.git");
        await Assert.That(source.Ref).IsEqualTo("refs/heads/main");
    }

    [Test]
    public async Task AddSource_WhenRefMissing_PreservesManifest()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var original = File.ReadAllText(path);

        var exitCode = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "source",
                "git",
                "upstream",
                "https://github.com/example/standards.git",
            ],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task AddSource_WhenManifestDirectorySelected_UpdatesSelectedManifest()
    {
        using var workspace = new TestWorkspace();
        var packDirectory = Path.Combine(workspace.Path, "pack");
        Directory.CreateDirectory(packDirectory);
        var initExit = await workspace.Application.RunAsync(
            [
                "pack",
                "init",
                "--workspace",
                packDirectory,
                "--id",
                "example",
                "--author",
                "Example Author",
                "--license",
                "MIT",
            ],
            workspace.Path
        );

        var addExit = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "source",
                "git",
                "upstream",
                "https://github.com/example/standards.git",
                "--ref",
                "main",
                "--manifest",
                "pack",
            ],
            workspace.Path
        );
        var selected = await new PackManifestStore(workspace.FileSystem).LoadAsync(packDirectory);

        await Assert.That(initExit).IsEqualTo(0);
        await Assert.That(addExit).IsEqualTo(0);
        await Assert.That(selected.Value?.Sources).ContainsKey("upstream");
        await Assert
            .That(File.Exists(Path.Combine(workspace.Path, PackManifestStore.FileName)))
            .IsFalse();
    }

    [Test]
    public async Task AddExternalGlob_WhenOptionsValid_PersistsCanonicalSelector()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "source",
                "git",
                "upstream",
                "https://github.com/example/standards.git",
                "--ref",
                "main",
            ],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "glob",
                @"docs\**\*.md",
                "--source",
                "upstream",
                "--exclude",
                @"docs\internal\**",
                "--exclude",
                @"docs\drafts\**",
                "--flatten",
                "--target",
                @".github\standards",
            ],
            workspace.Path
        );
        var selector = (await LoadAsync(workspace)).ManagedFiles.Single();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(selector.Source).IsEqualTo("upstream");
        await Assert.That(selector.Glob).IsEqualTo("docs/**/*.md");
        await Assert.That(selector.Exclude).IsEquivalentTo(["docs/internal/**", "docs/drafts/**"]);
        await Assert.That(selector.Flatten).IsTrue();
        await Assert.That(selector.Target).IsEqualTo(".github/standards");
    }

    [Test]
    public async Task AddExternalFile_WhenSourceAliasUnknown_PreservesManifestAndGuidesAuthor()
    {
        var console = new SpectreTestConsole();
        using var workspace = await CreateInitializedWorkspaceAsync(console);
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var original = File.ReadAllText(path);

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "add", "file", "README.md", "--source", "missing"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
        await Assert.That(console.Output).Contains("Pack source alias 'missing' is not declared");
    }

    [Test]
    public async Task AddExternalFile_WhenPathIsRooted_PreservesManifest()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "source",
                "git",
                "upstream",
                "https://github.com/example/standards.git",
                "--ref",
                "main",
            ],
            workspace.Path
        );
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var original = File.ReadAllText(path);

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "add", "file", "/etc/passwd", "--source", "upstream"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task Sources_WhenSourceReferenced_ListsSanitizedIdentityAndReferenceCount()
    {
        var console = new SpectreTestConsole();
        console.Profile.Width = 200;
        using var workspace = await CreateInitializedWorkspaceAsync(console);
        await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "source",
                "git",
                "upstream",
                "git@github.com:Example/Standards.git",
                "--ref",
                "main",
            ],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["pack", "add", "file", "README.md", "--source", "upstream"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["pack", "sources"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("github.com/example/standards");
        await Assert.That(console.Output).Contains("refs/heads/main");
        await Assert.That(console.Output).DoesNotContain("git@github.com");
        await Assert.That(console.Output).Contains("References");
    }

    [Test]
    public async Task RemoveSource_WhenReferenced_RefusesUntilSelectorRemoved()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "source",
                "git",
                "upstream",
                "https://github.com/example/standards.git",
                "--ref",
                "main",
            ],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["pack", "add", "file", "README.md", "--source", "upstream"],
            workspace.Path
        );
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var referenced = File.ReadAllText(path);

        var refusedExit = await workspace.Application.RunAsync(
            ["pack", "rm", "source", "upstream"],
            workspace.Path
        );
        var afterRefusal = File.ReadAllText(path);
        await workspace.Application.RunAsync(["pack", "rm", "README.md"], workspace.Path);
        var removedExit = await workspace.Application.RunAsync(
            ["pack", "remove", "source", "upstream"],
            workspace.Path
        );
        var manifest = await LoadAsync(workspace);

        await Assert.That(refusedExit).IsEqualTo(1);
        await Assert.That(afterRefusal).IsEqualTo(referenced);
        await Assert.That(removedExit).IsEqualTo(0);
        await Assert.That(manifest.Sources).DoesNotContainKey("upstream");
    }

    [Test]
    public async Task Remove_WhenSelectorEscapes_PreservesManifest()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        const string manifest =
            "id: example\nversion: 1.0.0\nmanagedFiles:\n- source: ../unsafe\n  target: unsafe\n";
        File.WriteAllText(path, manifest);

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "rm", "../unsafe"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(manifest);
    }

    [Test]
    public async Task HookCommands_WhenCommandAndFileScriptsAdded_PreserveLiteralArguments()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();

        var commandExit = await workspace.Application.RunAsync(
            ["pack", "add", "hook", "script", "command", "postInstall", "npm", "install"],
            workspace.Path
        );
        var fileExit = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "hook",
                "script",
                "file",
                "preInstall",
                @"scripts\setup.ps1",
                "pwsh",
                "-NoProfile",
            ],
            workspace.Path
        );
        var manifest = await LoadAsync(workspace);

        await Assert.That(commandExit).IsEqualTo(0);
        await Assert.That(fileExit).IsEqualTo(0);
        var hooks =
            manifest.Hooks ?? throw new InvalidOperationException("Hooks were not persisted.");
        var postInstall =
            hooks.PostInstall
            ?? throw new InvalidOperationException("Post-install hooks were not persisted.");
        var preInstall =
            hooks.PreInstall
            ?? throw new InvalidOperationException("Pre-install hooks were not persisted.");
        await Assert.That(postInstall.Single().Command).IsEqualTo("npm");
        await Assert.That(postInstall.Single().Arguments).IsEquivalentTo(["install"]);
        await Assert.That(preInstall.Single().File).IsEqualTo("scripts/setup.ps1");
        await Assert.That(preInstall.Single().Runner).IsEqualTo("pwsh");
    }

    [Test]
    public async Task HookCommands_WhenAppendedAndReplaced_PreservePositionAndInstructionSettings()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        await workspace.Application.RunAsync(
            ["pack", "add", "hook", "script", "command", "preInstall", "first"],
            workspace.Path
        );
        var instructionExit = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "hook",
                "instruction",
                "preInstall",
                @"instructions\setup.md",
                "--templating",
            ],
            workspace.Path
        );
        var replaceExit = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "hook",
                "script",
                "file",
                "preInstall",
                @"scripts\setup.ps1",
                "pwsh",
                "--replace",
                "1",
            ],
            workspace.Path
        );
        var manifest = await LoadAsync(workspace);

        await Assert.That(instructionExit).IsEqualTo(0);
        await Assert.That(replaceExit).IsEqualTo(0);
        var hooks =
            manifest.Hooks ?? throw new InvalidOperationException("Hooks were not persisted.");
        var preInstall =
            hooks.PreInstall
            ?? throw new InvalidOperationException("Pre-install hooks were not persisted.");
        await Assert.That(preInstall).Count().IsEqualTo(2);
        await Assert.That(preInstall[0].File).IsEqualTo("scripts/setup.ps1");
        await Assert.That(preInstall[1].File).IsEqualTo("instructions/setup.md");
        await Assert.That(preInstall[1].Templating).IsTrue();
    }

    [Test]
    public async Task HookCommands_WhenPositionInvalid_PreserveManifestBytes()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        await workspace.Application.RunAsync(
            ["pack", "add", "hook", "script", "command", "preInstall", "first"],
            workspace.Path
        );
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var original = File.ReadAllText(path);

        var replaceExit = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "hook",
                "script",
                "command",
                "preInstall",
                "replacement",
                "--replace",
                "2",
            ],
            workspace.Path
        );
        var removeExit = await workspace.Application.RunAsync(
            ["pack", "rm", "hook", "preInstall", "0"],
            workspace.Path
        );

        await Assert.That(replaceExit).IsEqualTo(1);
        await Assert.That(removeExit).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task HookCommands_WhenInstructionInvalid_PreserveManifestBytes()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var original = File.ReadAllText(path);

        var exitCode = await workspace.Application.RunAsync(
            ["pack", "add", "hook", "instruction", "preInstall", "instructions/setup.txt"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task HookCommands_WhenLegacySyntaxUsed_RejectsWithoutMutation()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var original = File.ReadAllText(path);

        var addExit = await workspace.Application.RunAsync(
            ["pack", "add", "script", "command", "preInstall", "tool"],
            workspace.Path
        );
        var listExit = await workspace.Application.RunAsync(["pack", "scripts"], workspace.Path);

        await Assert.That(addExit).IsEqualTo(1);
        await Assert.That(listExit).IsEqualTo(1);
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task Hooks_WhenMixedHooksExist_ListsDeclarationOrderAndEffectiveSettings()
    {
        var console = new SpectreTestConsole();
        console.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: console);
        await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example", "--author", "Example", "--license", "MIT"],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["pack", "add", "hook", "instruction", "preInstall", "instructions/setup.md"],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["pack", "add", "hook", "script", "command", "preInstall", "tool", "two words"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["pack", "hooks"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("instructions/setup.md; templating: disabled");
        await Assert.That(console.Output).Contains("tool \"two words\"");
        await Assert
            .That(console.Output.LastIndexOf("instructions/setup.md", StringComparison.Ordinal))
            .IsLessThan(console.Output.LastIndexOf("tool \"two words\"", StringComparison.Ordinal));
    }

    [Test]
    public async Task Hooks_WhenNoHooksExist_ReportsExplicitEmptyState()
    {
        var console = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: console);
        await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example", "--author", "Example", "--license", "MIT"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["pack", "hooks"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("No lifecycle hooks declared.");
    }

    [Test]
    public async Task MetadataReferenceTagAndParameter_WhenAuthored_RoundTrip()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();

        var metadataExit = await workspace.Application.RunAsync(
            ["pack", "set", "homepage", "https://example.test/pack"],
            workspace.Path
        );
        var tagExit = await workspace.Application.RunAsync(
            ["pack", "add", "tag", "quality"],
            workspace.Path
        );
        var parameterExit = await workspace.Application.RunAsync(
            [
                "pack",
                "set",
                "parameter",
                "environment",
                "enum",
                "--value",
                "dev",
                "--value",
                "prod",
            ],
            workspace.Path
        );
        var referenceExit = await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "reference",
                "foundation",
                "1.0.0",
                "--parameter",
                "enabled=true",
                "--disable-hook",
                "postInstall",
            ],
            workspace.Path
        );
        var manifest = await LoadAsync(workspace);

        await Assert
            .That(
                new[] { metadataExit, tagExit, parameterExit, referenceExit }.All(code => code == 0)
            )
            .IsTrue();
        await Assert.That(manifest.Homepage).IsEqualTo("https://example.test/pack");
        await Assert.That(manifest.Tags).Contains("quality");
        await Assert
            .That(manifest.Parameters["environment"].Values)
            .IsEquivalentTo(["dev", "prod"]);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, PackManifestStore.FileName)))
            .Contains("enabled: true");
    }

    [Test]
    public async Task RemoveCommands_WhenEntriesExist_RemoveOnlySelectedEntries()
    {
        using var workspace = await CreateInitializedWorkspaceAsync();
        await workspace.Application.RunAsync(["pack", "add", "file", "README.md"], workspace.Path);
        await workspace.Application.RunAsync(
            ["pack", "add", "hook", "script", "command", "postInstall", "npm", "install"],
            workspace.Path
        );
        var fileExit = await workspace.Application.RunAsync(
            ["pack", "rm", "README.md"],
            workspace.Path
        );
        var scriptExit = await workspace.Application.RunAsync(
            ["pack", "rm", "hook", "postInstall", "1"],
            workspace.Path
        );
        var manifest = await LoadAsync(workspace);

        await Assert.That(fileExit).IsEqualTo(0);
        await Assert.That(scriptExit).IsEqualTo(0);
        await Assert.That(manifest.ManagedFiles).IsEmpty();
        await Assert.That(manifest.Hooks!.PostInstall).IsNull();
    }

    [Test]
    public async Task ShowAndValidate_WhenManifestValid_ReportLocalState()
    {
        var console = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: console);
        File.WriteAllText(Path.Combine(workspace.Path, "README.md"), "example");
        await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example", "--author", "Example Author", "--license", "MIT"],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["pack", "add", "file", "README.md", "--target", "docs/README.md"],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["pack", "add", "hook", "script", "command", "postInstall", "npm", "install"],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["pack", "set", "description", "Example description"],
            workspace.Path
        );

        var listExit = await workspace.Application.RunAsync(["pack", "list"], workspace.Path);
        var showExit = await workspace.Application.RunAsync(["pack", "show"], workspace.Path);
        var scriptsExit = await workspace.Application.RunAsync(["pack", "hooks"], workspace.Path);
        var validateExit = await workspace.Application.RunAsync(
            ["pack", "validate"],
            workspace.Path
        );

        await Assert.That(listExit).IsEqualTo(0);
        await Assert.That(showExit).IsEqualTo(0);
        await Assert.That(scriptsExit).IsEqualTo(0);
        await Assert.That(validateExit).IsEqualTo(0);
        await Assert.That(console.Output).Contains("example");
        await Assert.That(console.Output).Contains("Example description");
        await Assert.That(console.Output).Contains("README.md");
        await Assert.That(console.Output).Contains("docs/README.md");
        await Assert.That(console.Output).Contains("Managed files");
        await Assert.That(console.Output).Contains("References");
        await Assert.That(console.Output).Contains("Parameters");
        await Assert.That(console.Output).Contains("postInstall");
        await Assert.That(console.Output).Contains("npm install");
        await Assert.That(console.Output).Contains("Manifest valid.");
    }

    [Test]
    public async Task Validate_WhenLocalSourceFileIsMissing_ReportsFailure()
    {
        var console = new SpectreTestConsole();
        using var workspace = await CreateInitializedWorkspaceAsync(console);
        await workspace.Application.RunAsync(["pack", "add", "file", "missing.md"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["pack", "validate"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(console.Output).Contains("source file 'missing.md' is unavailable");
    }

    [Test]
    public async Task Validate_WhenExternalSourceIsUnused_WarnsAndSucceeds()
    {
        var console = new SpectreTestConsole();
        using var workspace = await CreateInitializedWorkspaceAsync(console);
        await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "source",
                "git",
                "upstream",
                "https://github.com/example/standards.git",
                "--ref",
                "main",
            ],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["pack", "validate"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).Contains("Source alias 'upstream' is unused");
        await Assert.That(console.Output).Contains("Manifest valid.");
    }

    [Test]
    [Arguments(true, 0)]
    [Arguments(false, 1)]
    public async Task Validate_WhenExternalFileReachabilityVaries_ReturnsExpectedResult(
        bool createExternalFile,
        int expectedExitCode
    )
    {
        var runner = new PackValidationGitProcessRunner(createExternalFile);
        using var workspace = new TestWorkspace(gitProcessRunner: runner);
        await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example", "--author", "Example", "--license", "MIT"],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            [
                "pack",
                "add",
                "source",
                "git",
                "upstream",
                "https://github.com/example/standards.git",
                "--ref",
                "main",
            ],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["pack", "add", "file", "README.md", "--source", "upstream"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["pack", "validate"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(expectedExitCode);
    }

    private static async Task<TestWorkspace> CreateInitializedWorkspaceAsync(
        SpectreTestConsole? console = null
    )
    {
        var workspace = new TestWorkspace(ansiConsole: console);
        var exitCode = await workspace.Application.RunAsync(
            ["pack", "init", "--id", "example", "--author", "Example Author", "--license", "MIT"],
            workspace.Path
        );
        if (exitCode != 0)
        {
            workspace.Dispose();
            throw new InvalidOperationException("Unable to initialize test pack.");
        }

        return workspace;
    }

    private static async Task<PackManifest> LoadAsync(TestWorkspace workspace)
    {
        var result = await new PackManifestStore(workspace.FileSystem).LoadAsync(workspace.Path);
        return result.Value ?? throw new InvalidOperationException(result.Error);
    }

    private sealed class PackValidationGitProcessRunner(bool createExternalFile) : IGitProcessRunner
    {
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
                return Success("1111111111111111111111111111111111111111\trefs/heads/main");
            }

            if (createExternalFile && arguments.Contains("checkout", StringComparer.Ordinal))
            {
                File.WriteAllText(Path.Combine(arguments[1], "README.md"), "external content");
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
