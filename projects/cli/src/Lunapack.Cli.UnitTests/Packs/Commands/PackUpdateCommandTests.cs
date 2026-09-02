using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Packs.Commands;

public sealed class PackUpdateCommandTests
{
    [Test]
    public async Task CachePromptedParameters_WhenPromptRepeated_ReusesFirstAnswer()
    {
        var callbackCount = 0;
        var prompt = new PackParameterPrompt(
            "includeDependency",
            new PackParameterDefinition(
                PackParameterType.Bool,
                false,
                [],
                "Include dependency",
                null
            )
        );
        var cached = PackUpdateService.CachePromptedParameters(prompts =>
        {
            callbackCount++;
            return prompts.ToDictionary(
                requested => requested.Id,
                _ => (IReadOnlyList<string>)["false"],
                StringComparer.Ordinal
            );
        });

        var previewAnswer = cached!([prompt]);
        var applyAnswer = cached([prompt]);

        await Assert.That(callbackCount).IsEqualTo(1);
        await Assert.That(previewAnswer["includeDependency"]).IsEquivalentTo(["false"]);
        await Assert.That(applyAnswer["includeDependency"]).IsEquivalentTo(["false"]);
    }

    [Test]
    public async Task Update_PromptDeclinesOneAvailableRoot_UpdatesOnlyConfirmedRoot()
    {
        var packUpdatePrompter = new TestPackUpdatePrompter([false, true]);
        using var workspace = new TestWorkspace(packUpdatePrompter);
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        CreateVersionedPackSource(workspace.Path, "csharpier", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        await workspace.Application.RunAsync(["install", "csharpier@1.0.0"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update", "-p"], workspace.Path);
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(packUpdatePrompter.PromptedIds).IsEquivalentTo(["csharpier", "dotnet"]);
        await Assert
            .That(
                state
                    .LockFile.Packs.Single(pack =>
                        string.Equals(pack.Id, "csharpier", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsEqualTo("1.0.0");
        await Assert
            .That(
                state
                    .LockFile.Packs.Single(pack =>
                        string.Equals(pack.Id, "dotnet", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsEqualTo("2.0.0");
    }

    [Test]
    public async Task Update_NamedVersionlessRequest_UpdatesLatestAndPreservesDestination()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(
            ["install", "dotnet@1.0.0", "-d", "docs/guidance"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "docs", "guidance", "dotnet.txt")))
            .IsEqualTo("2.0.0");
        var projectState = state.RequireValue();
        await Assert.That(projectState.Configuration.Packs.Single().Version).IsNull();
        await Assert
            .That(projectState.Configuration.Packs.Single().Destination)
            .IsEqualTo("docs/guidance");
        await Assert.That(projectState.LockFile.Packs.Single().Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task Scenario_UpdateSucceeds_ReportsManagedFileChanges()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var outputStart = ansiConsole.Output.Length;

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);
        var output = ansiConsole.Output[outputStart..];

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).Contains("File changes");
        await Assert.That(output).Contains("Copy");
        await Assert.That(output).Contains("dotnet.txt");
    }

    [Test]
    public async Task Scenario_UpdateReleaseExistsInMultipleSources_ReportsSelectedSource()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var primarySource = CreateNamedVersionedPackSource(
            workspace.Path,
            "primary-source",
            "dotnet",
            "1.0.0",
            "2.0.0"
        );
        var secondarySource = CreateNamedVersionedPackSource(
            workspace.Path,
            "secondary-source",
            "dotnet",
            "1.0.0",
            "2.0.0"
        );
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "primary", primarySource],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "secondary", secondarySource],
            workspace.Path
        );
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var outputStart = ansiConsole.Output.Length;

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);
        var output = ansiConsole.Output[outputStart..];

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).Contains("Selected source");
        await Assert.That(output).Contains("primary (local)");
    }

    [Test]
    public async Task Scenario_UpdateSuppressesFileChanges_HidesManagedFileChanges()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Width = 500;
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var outputStart = ansiConsole.Output.Length;

        var exitCode = await workspace.Application.RunAsync(
            ["update", "dotnet", "--no-file-change-output"],
            workspace.Path
        );
        var output = ansiConsole.Output[outputStart..];

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).DoesNotContain("File changes");
    }

    [Test]
    public async Task Update_MultipleNamedReferences_UpdatesEachRequestedRoot()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        CreateVersionedPackSource(workspace.Path, "csharpier", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(
            ["install", "dotnet@1.0.0", "csharpier@1.0.0"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["update", "csharpier", "dotnet"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(state.RequireValue().LockFile.Packs.Select(pack => pack.Version))
            .IsEquivalentTo(["2.0.0", "2.0.0"]);
    }

    [Test]
    public async Task Update_NamedExplicitRequest_PersistsSelectedManifestVersion()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["update", "dotnet@2.0.0"],
            workspace.Path
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        var projectState = state.RequireValue();
        await Assert.That(projectState.Configuration.Packs.Single().Version).IsEqualTo("2.0.0");
        await Assert.That(projectState.LockFile.Packs.Single().Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task Update_NamedExplicitUnavailableVersion_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["update", "dotnet@2.0.0"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Update_NamedVersionlessCurrentRelease_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Update_NamedPreflightFailure_LeavesManagedTargetAndProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        SetManagedFileStrategy(workspace.Path, "dotnet", "2.0.0", "copy", "fail-if-exists");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);
        var targetPath = Path.Combine(workspace.Path, "dotnet.txt");

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("1.0.0");
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Update_NamedUninstalledPack_LeavesProjectStateUnchanged()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update", "dotnet"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Update_AllAvailableRoots_UpdatesCompleteResolvedGraph()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        CreateVersionedPackSource(workspace.Path, "csharpier", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        await workspace.Application.RunAsync(["install", "csharpier@1.0.0"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update"], workspace.Path);
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        var projectState = state.RequireValue();
        await Assert
            .That(
                projectState
                    .LockFile.Packs.Single(pack =>
                        string.Equals(pack.Id, "dotnet", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsEqualTo("2.0.0");
        await Assert
            .That(
                projectState
                    .LockFile.Packs.Single(pack =>
                        string.Equals(pack.Id, "csharpier", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsEqualTo("2.0.0");
        await Assert
            .That(
                projectState
                    .Configuration.Packs.Single(pack =>
                        string.Equals(pack.Id, "dotnet", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsNull();
        await Assert
            .That(
                projectState
                    .Configuration.Packs.Single(pack =>
                        string.Equals(pack.Id, "csharpier", StringComparison.Ordinal)
                    )
                    .Version
            )
            .IsNull();
    }

    [Test]
    public async Task Scenario_NamedUpdateDryRun_PreservesTargetStateAndBackup()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        SetManagedFileStrategy(workspace.Path, "dotnet", "2.0.0", "copy", "backup-and-overwrite");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);
        var targetPath = Path.Combine(workspace.Path, "dotnet.txt");

        var exitCode = await workspace.Application.RunAsync(
            ["update", "dotnet", "-D"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("1.0.0");
        await Assert.That(File.Exists($"{targetPath}.1")).IsFalse();
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Scenario_UpdateAllDryRun_PreservesManagedTargetsAndProjectState()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        CreateVersionedPackSource(workspace.Path, "csharpier", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        await workspace.Application.RunAsync(["install", "csharpier@1.0.0"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update", "-D"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "dotnet.txt")))
            .IsEqualTo("1.0.0");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "csharpier.txt")))
            .IsEqualTo("1.0.0");
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task Scenario_PromptedUpdateDryRun_PreservesDeclinedAndConfirmedRoots()
    {
        var packUpdatePrompter = new TestPackUpdatePrompter([false, true]);
        using var workspace = new TestWorkspace(packUpdatePrompter);
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        CreateVersionedPackSource(workspace.Path, "csharpier", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);
        await workspace.Application.RunAsync(["install", "csharpier@1.0.0"], workspace.Path);
        var initialState = await ReadStateAsync(workspace.Path);

        var exitCode = await workspace.Application.RunAsync(["update", "-p", "-D"], workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(packUpdatePrompter.PromptedIds).IsEquivalentTo(["csharpier", "dotnet"]);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "dotnet.txt")))
            .IsEqualTo("1.0.0");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "csharpier.txt")))
            .IsEqualTo("1.0.0");
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task UpdateDryRun_WhenOptionalParameterControlsReference_PromptsBeforePlanning()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = Path.Combine(workspace.Path, "source");
        var rootOne = Path.Combine(sourcePath, "root-1.0.0");
        var rootTwo = Path.Combine(sourcePath, "root-2.0.0");
        var dependency = Path.Combine(sourcePath, "dependency-1.0.0");
        Directory.CreateDirectory(rootOne);
        Directory.CreateDirectory(rootTwo);
        Directory.CreateDirectory(dependency);
        const string parameter =
            "parameters:\n  includeDependency:\n    type: bool\n    default: true\n    displayName: Include dependency\n";
        const string reference = "packs:\n  - id: dependency\n    version: 1.0.0\n";
        File.WriteAllText(
            Path.Combine(rootOne, "pack.yml"),
            $"id: root\nversion: 1.0.0\nlicense: MIT\nauthor: Example Author\n{parameter}{reference}"
        );
        File.WriteAllText(
            Path.Combine(rootTwo, "pack.yml"),
            $"id: root\nversion: 2.0.0\nlicense: MIT\nauthor: Example Author\n{parameter}{reference}    condition: includeDependency\n"
        );
        File.WriteAllText(
            Path.Combine(dependency, "pack.yml"),
            "id: dependency\nversion: 1.0.0\nlicense: MIT\nauthor: Example Author\nparameters:\n  branchDetail:\n    type: string\n    displayName: Branch detail\nmanagedFiles:\n  - source: dependency.txt\n    target: dependency.txt\n"
        );
        File.WriteAllText(Path.Combine(dependency, "dependency.txt"), "dependency");
        await ConfigureSourceAsync(workspace, "source");
        await workspace.Application.RunAsync(
            ["install", "root@1.0.0", "--parameter", "branchDetail=initial"],
            workspace.Path
        );
        var initialState = await ReadStateAsync(workspace.Path);
        ansiConsole.Input.PushTextWithEnter("n");

        var exitCode = await workspace.Application.RunAsync(
            ["update", "root@2.0.0", "--dry-run"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("Include dependency");
        await Assert.That(ansiConsole.Output).DoesNotContain("Branch detail");
        await Assert.That(ansiConsole.Output).Contains("dependency.txt");
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
        var skippedOutputStart = ansiConsole.Output.Length;

        var skippedExitCode = await workspace.Application.RunAsync(
            [
                "update",
                "root@2.0.0",
                "--dry-run",
                "--skip-parameters",
                "--parameter",
                "includeDependency=false",
            ],
            workspace.Path
        );
        var skippedOutput = ansiConsole.Output[skippedOutputStart..];

        await Assert.That(skippedExitCode).IsEqualTo(0);
        await Assert.That(skippedOutput).DoesNotContain("Include dependency");
        await Assert.That(skippedOutput).DoesNotContain("Branch detail");
        await Assert.That(skippedOutput).Contains("Delete");
        await Assert.That(skippedOutput).Contains("dependency.txt");
        await Assert.That(await ReadStateAsync(workspace.Path)).IsEqualTo(initialState);
    }

    [Test]
    public async Task UpdateDryRun_WhenRequiredWhenActiveAndPromptsSkipped_ReturnsFailure()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = Path.Combine(workspace.Path, "source");
        foreach (var version in new[] { "1.0.0", "2.0.0" })
        {
            var packDirectory = Path.Combine(sourcePath, $"root-{version}");
            Directory.CreateDirectory(packDirectory);
            File.WriteAllText(
                Path.Combine(packDirectory, "pack.yml"),
                $"id: root\nversion: {version}\nlicense: MIT\nauthor: Example Author\nparameters:\n  enabled:\n    type: bool\n    default: true\n  detail:\n    type: string\n    requiredWhen: enabled\nmanagedFiles: []\n"
            );
        }

        await ConfigureSourceAsync(workspace, "source");
        await workspace.Application.RunAsync(
            ["install", "root@1.0.0", "--parameter", "detail=initial"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["update", "root@2.0.0", "--dry-run", "--skip-parameters"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).Contains("detail");
    }

    [Test]
    public async Task Update_WhenNoVariablesSpecified_UsesDeclaredDefault()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = Path.Combine(workspace.Path, "source");
        foreach (var version in new[] { "1.0.0", "2.0.0" })
        {
            var packDirectory = Path.Combine(sourcePath, $"root-{version}");
            Directory.CreateDirectory(packDirectory);
            File.WriteAllText(
                Path.Combine(packDirectory, "pack.yml"),
                $"id: root\nversion: {version}\nlicense: MIT\nauthor: Example Author\nparameters:\n  label:\n    type: string\n    default: fallback\nmanagedFiles:\n  - source: content.txt\n    target: output.txt\n    template: true\n"
            );
            File.WriteAllText(Path.Combine(packDirectory, "content.txt"), "{{ label }}");
        }

        await ConfigureSourceAsync(workspace, "source");
        await workspace.Application.RunAsync(
            ["variables", "set", "label", "configured"],
            workspace.Path
        );
        await workspace.Application.RunAsync(["install", "root@1.0.0"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["update", "root@2.0.0", "--no-variables"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "output.txt")))
            .IsEqualTo("fallback");
    }

    [Test]
    public async Task Update_WhenVariableSkipped_UsesDefaultForOnlyThatParameter()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = Path.Combine(workspace.Path, "source");
        foreach (var version in new[] { "1.0.0", "2.0.0" })
        {
            var packDirectory = Path.Combine(sourcePath, $"root-{version}");
            Directory.CreateDirectory(packDirectory);
            File.WriteAllText(
                Path.Combine(packDirectory, "pack.yml"),
                $"id: root\nversion: {version}\nlicense: MIT\nauthor: Example Author\nparameters:\n  first:\n    type: string\n    default: first-default\n  second:\n    type: string\n    default: second-default\nmanagedFiles:\n  - source: content.txt\n    target: output.txt\n    template: true\n"
            );
            File.WriteAllText(
                Path.Combine(packDirectory, "content.txt"),
                "{{ first }} {{ second }}"
            );
        }

        await ConfigureSourceAsync(workspace, "source");
        await workspace.Application.RunAsync(
            ["variables", "set", "first", "configured-first"],
            workspace.Path
        );
        await workspace.Application.RunAsync(
            ["variables", "set", "second", "configured-second"],
            workspace.Path
        );
        await workspace.Application.RunAsync(["install", "root@1.0.0"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            ["update", "root@2.0.0", "--skip-variable", "first"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "output.txt")))
            .IsEqualTo("first-default configured-second");
    }

    [Test]
    public async Task Update_WhenRemappingSaved_PersistsOnRequestedRoot()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(
            [
                "update",
                "dotnet@2.0.0",
                "--remap-file",
                "dotnet.txt=docs/dotnet.txt",
                "--save-remap",
            ],
            workspace.Path
        );
        var state = (await workspace.StateStore.LoadAsync(workspace.Path)).RequireValue();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspace.Path, "docs", "dotnet.txt")))
            .IsEqualTo("2.0.0");
        await Assert
            .That(state.Configuration.Packs.Single().Remap?.Files["dotnet.txt"])
            .IsEqualTo("docs/dotnet.txt");
    }

    [Test]
    [Arguments(new[] { "update", "dotnet", "--skip-parameters" }, "--dry-run")]
    [Arguments(new[] { "update", "--save-remap" }, "requires")]
    [Arguments(new[] { "update", "--remap-file", "a=b" }, "exactly one")]
    public async Task Update_WhenConfigurationOptionsInvalid_ReturnsFailure(
        string[] arguments,
        string expectedError
    )
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        var sourcePath = CreateVersionedPackSource(workspace.Path, "dotnet", "1.0.0", "2.0.0");
        await ConfigureSourceAsync(workspace, sourcePath);
        await workspace.Application.RunAsync(["install", "dotnet@1.0.0"], workspace.Path);

        var exitCode = await workspace.Application.RunAsync(arguments, workspace.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).Contains(expectedError);
    }

    private static async Task ConfigureSourceAsync(TestWorkspace workspace, string sourcePath)
    {
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", sourcePath],
            workspace.Path
        );
    }

    private static string CreateVersionedPackSource(
        string projectDirectory,
        string id,
        params string[] versions
    ) => CreateNamedVersionedPackSource(projectDirectory, "source", id, versions);

    private static string CreateNamedVersionedPackSource(
        string projectDirectory,
        string sourceDirectory,
        string id,
        params string[] versions
    )
    {
        var sourcePath = Path.Combine(projectDirectory, sourceDirectory);
        foreach (var version in versions)
        {
            var packDirectory = Path.Combine(sourcePath, $"{id}-{version}");
            var templateDirectory = Path.Combine(packDirectory, "templates");
            Directory.CreateDirectory(templateDirectory);
            File.WriteAllText(
                Path.Combine(packDirectory, "pack.yml"),
                $"id: {id}\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: {id}.txt\n"
            );
            File.WriteAllText(Path.Combine(templateDirectory, "content.txt"), version);
        }

        return sourceDirectory;
    }

    private static void SetManagedFileStrategy(
        string projectDirectory,
        string id,
        string version,
        string type,
        string method
    )
    {
        var manifestPath = Path.Combine(projectDirectory, "source", $"{id}-{version}", "pack.yml");
        File.WriteAllText(
            manifestPath,
            $"id: {id}\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: {id}.txt\n    strategy:\n      type: {type}\n      method: {method}\n"
        );
    }

    private static async Task<string> ReadStateAsync(string projectDirectory) =>
        string.Concat(
            await File.ReadAllTextAsync(
                Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName)
            ),
            await File.ReadAllTextAsync(
                Path.Combine(projectDirectory, ProjectStateStore.LockFileName)
            )
        );

    private sealed class TestPackUpdatePrompter(IEnumerable<bool> responses) : IPackUpdatePrompter
    {
        private readonly Queue<bool> _responses = new(responses);

        public List<string> PromptedIds { get; } = [];

        public bool Confirm(AvailablePackUpdate update)
        {
            PromptedIds.Add(update.RequestedRoot.Id);
            return _responses.Dequeue();
        }
    }
}
