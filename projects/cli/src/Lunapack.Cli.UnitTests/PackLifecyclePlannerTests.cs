namespace Lunapack.Cli.UnitTests;

public sealed class PackLifecyclePlannerTests
{
    [Test]
    public async Task Plan_WhenGraphContainsNewRootAndDependency_OrdersInstallPhasesDependencyFirst()
    {
        var dependency = CreatePack("dependency", "1.0.0");
        var root = CreatePack("root", "1.0.0");

        var plan = PackLifecyclePlanner.Plan(
            new ResolvedPackGraph(
                [dependency, root],
                new HashSet<string>(["root"], StringComparer.Ordinal)
            ),
            new ProjectLockFile { SchemaVersion = 1 }
        );

        await Assert
            .That(plan.Changes.Select(change => change.Kind))
            .IsEquivalentTo([
                PackLifecyclePlan.ChangeKind.Install,
                PackLifecyclePlan.ChangeKind.Install,
            ]);
        await Assert.That(plan.Changes[0].IsDirectRoot).IsFalse();
        await Assert.That(plan.Changes[1].IsDirectRoot).IsTrue();
        await Assert.That(plan.PreMutation[0].IncomingPack!.Manifest.Id).IsEqualTo("dependency");
        await Assert.That(plan.PreMutation[1].IncomingPack!.Manifest.Id).IsEqualTo("root");
        await Assert.That(plan.PostMutation[0].IncomingPack!.Manifest.Id).IsEqualTo("dependency");
        await Assert.That(plan.PostMutation[1].IncomingPack!.Manifest.Id).IsEqualTo("root");
    }

    [Test]
    public async Task Plan_WhenIncomingVersionChanges_ClassifiesUpdate()
    {
        var plan = PackLifecyclePlanner.Plan(
            new ResolvedPackGraph([CreatePack("example", "2.0.0")]),
            CreateLockFile(CreatePreviousPack("example", "1.0.0"))
        );

        await Assert
            .That(plan.Changes.Single().Kind)
            .IsEqualTo(PackLifecyclePlan.ChangeKind.Update);
    }

    [Test]
    public async Task Plan_WhenIncomingVersionMatches_ClassifiesUnchanged()
    {
        var plan = PackLifecyclePlanner.Plan(
            new ResolvedPackGraph([CreatePack("example", "1.0.0")]),
            CreateLockFile(CreatePreviousPack("example", "1.0.0"))
        );

        await Assert
            .That(plan.Changes.Single().Kind)
            .IsEqualTo(PackLifecyclePlan.ChangeKind.Unchanged);
        await Assert.That(plan.PreMutation).IsEmpty();
    }

    [Test]
    public async Task Plan_WhenPreviousPackIsAbsent_ClassifiesRemoved()
    {
        var plan = PackLifecyclePlanner.Plan(
            new ResolvedPackGraph([]),
            CreateLockFile(CreatePreviousPack("removed", "1.0.0"))
        );

        await Assert
            .That(plan.Changes.Single().Kind)
            .IsEqualTo(PackLifecyclePlan.ChangeKind.Removed);
        await Assert.That(plan.Changes.Single().PreviousPack!.Id).IsEqualTo("removed");
    }

    [Test]
    public async Task Plan_WhenTransientHasMultipleIncomingReferences_UsesDisabledHookUnion()
    {
        var shared = CreatePack("shared", "1.0.0");
        var firstRoot = CreatePack(
            "first",
            "1.0.0",
            new PackManifest.PackReference
            {
                Id = "shared",
                Version = "1.0.0",
                DisabledHooks = ["preInstall"],
            }
        );
        var secondRoot = CreatePack(
            "second",
            "1.0.0",
            new PackManifest.PackReference
            {
                Id = "shared",
                Version = "1.0.0",
                DisabledHooks = ["postInstall", "preInstall"],
            }
        );

        var plan = PackLifecyclePlanner.Plan(
            new ResolvedPackGraph(
                [shared, firstRoot, secondRoot],
                new HashSet<string>(["first", "second"], StringComparer.Ordinal)
            ),
            new ProjectLockFile { SchemaVersion = 1 }
        );

        var sharedChange = plan.Changes.Single(change => change.IncomingPack == shared);
        await Assert.That(sharedChange.IsDirectRoot).IsFalse();
        await Assert
            .That(sharedChange.DisabledHooks)
            .IsEquivalentTo(["preInstall", "postInstall"], StringComparer.Ordinal);
    }

    [Test]
    public async Task Plan_WhenSuppressedPackIsAlsoDirectRoot_PreservesDirectRootHooks()
    {
        var shared = CreatePack("shared", "1.0.0");
        var root = CreatePack(
            "root",
            "1.0.0",
            new PackManifest.PackReference
            {
                Id = "shared",
                Version = "1.0.0",
                DisabledHooks = ["preInstall"],
            }
        );

        var plan = PackLifecyclePlanner.Plan(
            new ResolvedPackGraph(
                [shared, root],
                new HashSet<string>(["shared", "root"], StringComparer.Ordinal)
            ),
            new ProjectLockFile { SchemaVersion = 1 }
        );

        var sharedChange = plan.Changes.Single(change => change.IncomingPack == shared);
        await Assert.That(sharedChange.IsDirectRoot).IsTrue();
        await Assert.That(sharedChange.DisabledHooks).IsEmpty();
    }

    private static DiscoveredPack CreatePack(
        string id,
        string version,
        params PackManifest.PackReference[] references
    ) =>
        new(
            "source",
            $"source/{id}",
            new PackManifest
            {
                Id = id,
                Version = version,
                Packs = [.. references],
            },
            "local",
            ConfiguredSourceIdentity.CreateLocal("source")
        );

    private static ProjectLockFile CreateLockFile(ProjectLockFile.ResolvedPack pack) =>
        new() { SchemaVersion = 1, Packs = [pack] };

    private static ProjectLockFile.ResolvedPack CreatePreviousPack(string id, string version) =>
        new()
        {
            Id = id,
            Version = version,
            PackPath = id,
            SourceName = "local",
            SourceIdentity = ConfiguredSourceIdentity.CreateLocal("source"),
            SourcePath = "source",
        };
}
