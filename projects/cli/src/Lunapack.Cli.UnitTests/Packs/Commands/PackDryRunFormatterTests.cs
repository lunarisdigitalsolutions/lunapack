using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Commands;
using Lunapack.Cli.Packs.Instructions;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.UnitTests.Packs.Commands;

public sealed class PackDryRunFormatterTests
{
    [Test]
    public async Task Scenario_InstallPreviewHasNoManagedFiles_IncludesSelectedRelease()
    {
        var output = PackDryRunFormatter.FormatInstall(
            new PackInstallDryRunResult(new PackReference("empty", "2.0.0"), new PackUpdatePlan([]))
        );

        await Assert
            .That(output)
            .IsEquivalentTo([
                "[bold]Install plan[/]",
                "[cyan]*[/] Selected release  [bold]empty@2.0.0[/]",
            ]);
    }

    [Test]
    public async Task Scenario_InstallPreviewHasMultipleSources_ReportsSelectedSource()
    {
        var output = PackDryRunFormatter.FormatInstall(
            new PackInstallDryRunResult(
                new PackReference("example", "2.0.0"),
                new PackUpdatePlan([]),
                new PackSourceSelection("example", "primary", "git")
            )
        );

        await Assert.That(output).Contains("[cyan]>[/] Selected source   [bold]primary[/] (git)");
    }

    [Test]
    public async Task Scenario_UpdatePreviewHasMultipleSources_ReportsSelectedSource()
    {
        var output = PackDryRunFormatter.FormatUpdate(
            [
                new PackUpdateService.UpdateOutcome(
                    "example",
                    "1.0.0",
                    "2.0.0",
                    false,
                    new PackSourceSelection("example", "primary", "local")
                ),
            ],
            new PackUpdatePlan([])
        );

        await Assert.That(output).Contains("[cyan]>[/] Selected source   [bold]primary[/] (local)");
    }

    [Test]
    public async Task Scenario_UpdatePreviewHasNoActions_ReportsNoUpdates()
    {
        var output = PackDryRunFormatter.FormatUpdate([], new PackUpdatePlan([]));

        await Assert
            .That(output)
            .IsEquivalentTo(["[bold]Update plan[/]", "[grey]-[/] No updates are available."]);
    }

    [Test]
    public async Task Scenario_PreviewHasRemappings_ReportsDefinitionSources()
    {
        var updatePlan = new PackUpdatePlan([])
        {
            Remappings =
            [
                new ManagedFileRemapping(
                    "example",
                    "docs/command.md",
                    "command/command.md",
                    ManagedFileRemappingOrigin.Command
                ),
                new ManagedFileRemapping(
                    "example",
                    "docs/pack.md",
                    "pack/pack.md",
                    ManagedFileRemappingOrigin.Pack
                ),
                new ManagedFileRemapping(
                    "example",
                    "docs/project.md",
                    "project/project.md",
                    ManagedFileRemappingOrigin.Project
                ),
                new ManagedFileRemapping(
                    "example",
                    "docs/locked.md",
                    "locked/locked.md",
                    ManagedFileRemappingOrigin.Lock
                ),
            ],
        };
        var installOutput = PackDryRunFormatter.FormatInstall(
            new PackInstallDryRunResult(new PackReference("example", "2.0.0"), updatePlan)
        );
        var updateOutput = PackDryRunFormatter.FormatUpdate([], updatePlan);

        await Assert
            .That(installOutput)
            .Contains("remap: example docs/command.md -> command/command.md source: command line");
        await Assert
            .That(installOutput)
            .Contains(
                "remap: example docs/pack.md -> pack/pack.md source: pack 'example' in lunapack.yml"
            );
        await Assert
            .That(installOutput)
            .Contains(
                "remap: example docs/project.md -> project/project.md source: top-level remap in lunapack.yml"
            );
        await Assert
            .That(installOutput)
            .Contains(
                "remap: example docs/locked.md -> locked/locked.md source: lunapack-lock.yml"
            );
        await Assert
            .That(updateOutput)
            .IsEquivalentTo([
                "[bold]Update plan[/]",
                "[grey]-[/] No updates are available.",
                .. installOutput.Skip(2),
            ]);
    }

    [Test]
    public async Task Scenario_UpdatePreviewHasDeleteAction_IncludesSelectedReleaseAndAction()
    {
        var previousPack = new ProjectLockFile.ResolvedPack
        {
            Id = "example",
            Version = "1.0.0",
            SourcePath = "source",
            PackPath = "example",
            ManagedFiles =
            [
                new ProjectLockFile.ManagedFile { TargetPath = "obsolete.txt", Sha256 = "unused" },
            ],
        };
        var output = PackDryRunFormatter.FormatUpdate(
            [new PackUpdateService.UpdateOutcome("example", "1.0.0", "2.0.0", false)],
            new PackUpdatePlan([
                new DeleteManagedFileUpdateAction(
                    new ManagedRootOwner(
                        ManagedRootKind.Pack,
                        previousPack.Id,
                        previousPack.Version
                    ),
                    new ManagedRootFile(
                        previousPack.PackPath,
                        previousPack.ManagedFiles.Single().TargetPath,
                        previousPack.ManagedFiles.Single().TargetPath,
                        previousPack.ManagedFiles.Single().Sha256
                    ),
                    "C:\\project\\obsolete.txt"
                ),
            ])
        );

        await Assert
            .That(output)
            .IsEquivalentTo([
                "[bold]Update plan[/]",
                "[cyan]*[/] example  1.0.0 -> [bold]2.0.0[/]",
                string.Empty,
                "[bold]File changes[/]",
                "[red]-[/] Delete  obsolete.txt",
            ]);
    }

    [Test]
    public async Task Scenario_FileChangesContainEveryOperation_UsesUniqueSymbols()
    {
        var pack = new DiscoveredPack(
            "source",
            "source/example",
            new PackManifest { Id = "example", Version = "1.0.0" },
            "local",
            ConfiguredSourceIdentity.CreateLocal("source")
        );
        PlannedManagedFile CreateFile(string targetPath) =>
            new(
                pack,
                "source.txt",
                targetPath,
                [],
                $"C:\\project\\{targetPath}",
                targetPath,
                PackManifest.PackManagedFileStrategy.CopyOverwrite
            );
        var previousOwner = new ManagedRootOwner(ManagedRootKind.Pack, "example", "1.0.0");
        var output = PackDryRunFormatter
            .FormatFileChanges(
                new PackUpdatePlan([
                    new CreateManagedFileUpdateAction(CreateFile("create.txt")),
                    new CopyManagedFileUpdateAction(CreateFile("copy.txt"), null),
                    new BackupAndCopyManagedFileUpdateAction(
                        CreateFile("replace.txt"),
                        null,
                        "replace.txt.bak"
                    ),
                    new MergeLinesManagedFileUpdateAction(CreateFile("merge.txt"), null, []),
                    new SkipManagedFileUpdateAction(CreateFile("skip.txt"), null, []),
                    new DeleteManagedFileUpdateAction(
                        previousOwner,
                        new ManagedRootFile("example", "delete.txt", "delete.txt", "sha256"),
                        "C:\\project\\delete.txt"
                    ),
                ])
            )
            .ToArray();

        await Assert
            .That(output)
            .IsEquivalentTo([
                "[green]+[/] Create  create.txt",
                "[cyan]>[/] Copy    copy.txt",
                "[yellow]![/] Replace replace.txt  [grey](backup: replace.txt.bak)[/]",
                "[yellow]~[/] Merge   merge.txt [grey](lines)[/]",
                "[grey]=[/] Skip    skip.txt",
                "[red]-[/] Delete  delete.txt",
            ]);
    }

    [Test]
    public async Task Scenario_InstallPreviewHasLifecycleHooks_ReportsOrderAndSkipStatus()
    {
        var pack = new DiscoveredPack(
            "source",
            "source/example",
            new PackManifest { Id = "example", Version = "1.0.0" },
            "local",
            ConfiguredSourceIdentity.CreateLocal("source")
        );
        var preHook = new LifecycleHookInvocation(
            pack,
            LifecycleHook.PreInstall,
            new PackManifest.PackHook { Type = "script", Command = "cmd" },
            null
        );
        var postHook = preHook with { Hook = LifecycleHook.PostInstall };
        var output = PackDryRunFormatter.FormatInstall(
            new PackInstallDryRunResult(
                new PackReference("example", "1.0.0"),
                new PackUpdatePlan(
                    [],
                    new LifecycleDryRunPlan(ScriptExecutionMode.Skip, [preHook], [postHook], [])
                )
            )
        );

        await Assert.That(output).Contains("[bold]Lifecycle[/]");
        await Assert.That(output).Contains("[magenta]>[/] Scripts    skip");
        await Assert.That(output).Contains("[magenta]>[/] Pre-hook   example@1.0.0");
        await Assert.That(output).Contains("    Script       skipped (--scripts skip)");
        await Assert.That(output).Contains("[magenta]>[/] Post-hook  example@1.0.0");
        await Assert.That(output).DoesNotContain("preInstall");
        await Assert.That(output).DoesNotContain("postInstall");
    }

    [Test]
    public async Task Scenario_InstallPreviewHasInstruction_ReportsPreparedMetadata()
    {
        var pack = new DiscoveredPack(
            "source",
            "source/example",
            new PackManifest { Id = "example", Version = "1.0.0" },
            "local",
            ConfiguredSourceIdentity.CreateLocal("source")
        );
        var packedFile = new PackedHookFile(
            "instructions/setup.md",
            "C:\\snapshot\\setup.md",
            "HASH"
        );
        var instruction = new PreparedInstruction(
            packedFile,
            true,
            new InstructionDocument(
                string.Empty,
                [
                    new InstructionStep(1, null, "First", "One"),
                    new InstructionStep(2, null, "Second", "Two"),
                ]
            )
        );
        var hook = new LifecycleHookInvocation(
            pack,
            LifecycleHook.PostUpdate,
            new PackManifest.PackHook
            {
                Type = "instruction",
                File = "instructions/setup.md",
                Templating = true,
            },
            packedFile,
            Instruction: instruction
        );

        var output = PackDryRunFormatter.FormatInstall(
            new PackInstallDryRunResult(
                new PackReference("example", "1.0.0"),
                new PackUpdatePlan(
                    [],
                    new LifecycleDryRunPlan(ScriptExecutionMode.Prompt, [], [hook], [])
                )
            )
        );

        await Assert.That(output).Contains("[magenta]>[/] Post-hook  example@1.0.0");
        await Assert.That(output).Contains("    Instruction  instructions/setup.md");
        await Assert.That(output).Contains("    Templating   enabled");
        await Assert.That(output).Contains("    Steps        2");
        await Assert.That(output).DoesNotContain("postUpdate");
        await Assert.That(output).DoesNotContain("Press Enter to continue...");
    }

    [Test]
    public async Task Scenario_InstallPreviewHasScriptDenial_ReportsPolicyAndOrigins()
    {
        var pack = new DiscoveredPack(
            "source",
            "source/example",
            new PackManifest { Id = "example", Version = "1.0.0" },
            "local",
            ConfiguredSourceIdentity.CreateLocal("source")
        );
        var hook = new LifecycleHookInvocation(
            pack,
            LifecycleHook.PreInstall,
            new PackManifest.PackHook { Type = "script", Command = "cmd" },
            null
        );
        var output = PackDryRunFormatter.FormatInstall(
            new PackInstallDryRunResult(
                new PackReference("example", "1.0.0"),
                new PackUpdatePlan(
                    [],
                    new LifecycleDryRunPlan(
                        ScriptExecutionMode.Run,
                        [hook],
                        [],
                        [],
                        [ScriptDenialOrigin.Project, ScriptDenialOrigin.GlobalUser]
                    )
                )
            )
        );

        await Assert
            .That(output)
            .Contains("    Script       blocked (policy: project, global-user)");
    }

    [Test]
    public async Task Scenario_InstallPreviewHasTrustedScript_ReportsTrustScopes()
    {
        var pack = new DiscoveredPack(
            "source",
            "source/example",
            new PackManifest { Id = "example", Version = "1.0.0" },
            "local",
            ConfiguredSourceIdentity.CreateLocal("source")
        );
        var hook = new LifecycleHookInvocation(
            pack,
            LifecycleHook.PreInstall,
            new PackManifest.PackHook { Type = "script", Command = "cmd" },
            null
        );
        var output = PackDryRunFormatter.FormatInstall(
            new PackInstallDryRunResult(
                new PackReference("example", "1.0.0"),
                new PackUpdatePlan(
                    [],
                    new LifecycleDryRunPlan(
                        ScriptExecutionMode.Prompt,
                        [hook],
                        [],
                        [],
                        ScriptTrustScopes: new Dictionary<
                            LifecycleHookInvocation,
                            IReadOnlyList<TrustScope>
                        >
                        {
                            [hook] = [TrustScope.Project, TrustScope.LocalUser],
                        }
                    )
                )
            )
        );

        await Assert.That(output).Contains("    Script       allowed (trust: project, local-user)");
    }

    [Test]
    public async Task Scenario_InstallPreviewHasPreviousPack_OmitsLockedSource()
    {
        var previousPack = new ProjectLockFile.ResolvedPack
        {
            Id = "existing",
            Version = "1.0.0",
            PackPath = "existing",
            SourceIdentity = ConfiguredSourceIdentity.CreateLocal("source"),
        };
        var change = new PackLifecyclePlan.Entry(
            PackLifecyclePlan.ChangeKind.Removed,
            null,
            previousPack,
            false,
            new HashSet<string>(StringComparer.Ordinal)
        );
        var output = PackDryRunFormatter.FormatInstall(
            new PackInstallDryRunResult(
                new PackReference("new", "1.0.0"),
                new PackUpdatePlan(
                    [],
                    new LifecycleDryRunPlan(ScriptExecutionMode.Prompt, [], [], [change])
                )
            )
        );

        await Assert
            .That(output)
            .DoesNotContain("[grey]-[/] Locked source  existing local(path=source)");
    }

    [Test]
    public async Task Scenario_UpdatePreviewHasProposedSourceSwitch_ReportsBothIdentities()
    {
        var output = PackDryRunFormatter.FormatUpdate(
            [new PackUpdateService.UpdateOutcome("example", "1.0.0", "2.0.0", false)],
            new PackUpdatePlan([]),
            new LockedSourceUpdateSelector.SourceSwitch(
                "example",
                ConfiguredSourceIdentity.CreateLocal("first"),
                ConfiguredSourceIdentity.CreateLocal("second")
            )
        );

        await Assert
            .That(output)
            .Contains("[yellow]~[/] example  local(path=first) -> local(path=second)");
    }
}
