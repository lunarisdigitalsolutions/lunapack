namespace Lunapack.Cli.UnitTests;

public sealed class PackDryRunFormatterTests
{
    [Test]
    public async Task Scenario_InstallPreviewHasNoManagedFiles_IncludesSelectedRelease()
    {
        var output = PackDryRunFormatter.FormatInstall(
            new PackInstallDryRunResult(new PackReference("empty", "2.0.0"), new PackUpdatePlan([]))
        );

        await Assert.That(output).IsEquivalentTo(["Selected release: empty@2.0.0"]);
    }

    [Test]
    public async Task Scenario_UpdatePreviewHasNoActions_ReportsNoUpdates()
    {
        var output = PackDryRunFormatter.FormatUpdate([], new PackUpdatePlan([]));

        await Assert.That(output).IsEquivalentTo(["No updates are available."]);
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

        await Assert.That(output).IsEquivalentTo(["example 1.0.0 -> 2.0.0", "delete obsolete.txt"]);
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
            new PackManifest.LifecycleScript { Command = "cmd" },
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

        await Assert.That(output).Contains("scripts: skip");
        await Assert.That(output).Contains("pre-hook: example@1.0.0 preInstall consent: skipped");
        await Assert.That(output).Contains("post-hook: example@1.0.0 postInstall consent: skipped");
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
            .Contains("proposed source switch: example local(path=first) -> local(path=second)");
    }
}
