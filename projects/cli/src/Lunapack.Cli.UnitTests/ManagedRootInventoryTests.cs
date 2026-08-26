namespace Lunapack.Cli.UnitTests;

public sealed class ManagedRootInventoryTests
{
    [Test]
    public async Task FromLockFile_WhenPacksAndLinksAreResolved_ReturnsBothOwnerKinds()
    {
        var roots = ManagedRootInventory.FromLockFile(CreateLockFile());

        await Assert
            .That(roots.Select(root => root.Owner.Kind))
            .IsEquivalentTo([ManagedRootKind.Pack, ManagedRootKind.Link]);
        await Assert
            .That(
                roots
                    .Single(root => root.Owner.Kind is ManagedRootKind.Link)
                    .Files.Single()
                    .TargetPath
            )
            .IsEqualTo(".github/agents/CSharpExpert.agent.md");
    }

    [Test]
    public async Task CreateOwnershipMap_WhenTargetsOverlap_RecordsEveryOwner()
    {
        var lockFile = CreateLockFile();
        lockFile.Links["agents-expert"].Files[0].TargetPath = "docs/readme.md";

        var ownership = ManagedRootInventory.CreateOwnershipMap(lockFile);

        await Assert
            .That(ownership["docs/readme.md"].Select(owner => owner.Name))
            .IsEquivalentTo(["example", "agents-expert"]);
    }

    [Test]
    public async Task FindCrossRootCollision_WhenPackTargetIsOwnedByLink_ReportsLinkOwner()
    {
        var lockFile = CreateLockFile();
        var plannedRoots = new[]
        {
            new ManagedRoot(
                new ManagedRootOwner(ManagedRootKind.Pack, "example", "1.0.0"),
                "upstream",
                ConfiguredSourceIdentity.CreateLocal("packs"),
                null,
                [
                    new ManagedRootFile(
                        "packs/example",
                        ".github/agents/CSharpExpert.agent.md",
                        ".github/agents/CSharpExpert.agent.md",
                        new string('a', 64)
                    ),
                ]
            ),
        };

        var collision = ManagedRootInventory.FindCrossRootCollision(plannedRoots, lockFile);

        await Assert
            .That(collision)
            .IsEqualTo(
                "Target '.github/agents/CSharpExpert.agent.md' is already managed by link 'agents-expert'."
            );
    }

    [Test]
    public async Task FindCrossRootCollision_WhenPacksShareTargets_DoesNotReportCollision()
    {
        var lockFile = CreateLockFile();
        var plannedRoots = new[]
        {
            new ManagedRoot(
                new ManagedRootOwner(ManagedRootKind.Pack, "other", "1.0.0"),
                "upstream",
                ConfiguredSourceIdentity.CreateLocal("packs"),
                null,
                [
                    new ManagedRootFile(
                        "packs/other",
                        "docs/readme.md",
                        "docs/readme.md",
                        new string('a', 64)
                    ),
                ]
            ),
        };

        await Assert
            .That(ManagedRootInventory.FindCrossRootCollision(plannedRoots, lockFile))
            .IsNull();
    }

    private static ProjectLockFile CreateLockFile() =>
        new()
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "example",
                    Version = "1.0.0",
                    SourceName = "upstream",
                    SourceIdentity = ConfiguredSourceIdentity.CreateLocal("packs"),
                    SourcePath = "packs",
                    PackPath = "example",
                    ManagedFiles =
                    [
                        new ProjectLockFile.ManagedFile
                        {
                            DeclaredTargetPath = "docs/readme.md",
                            TargetPath = "docs/readme.md",
                            Sha256 = new string('b', 64),
                        },
                    ],
                },
            ],
            Links =
            {
                ["agents-expert"] = new ProjectLockFile.ResolvedLink
                {
                    SourceName = "upstream",
                    SourceIdentity = ConfiguredSourceIdentity.CreateLocal("packs"),
                    DefinitionSha256 = new string('c', 64),
                    Files =
                    [
                        new ProjectLockFile.LinkFile
                        {
                            SourcePath = "agents/CSharpExpert.agent.md",
                            DeclaredTargetPath = ".github/agents/CSharpExpert.agent.md",
                            TargetPath = ".github/agents/CSharpExpert.agent.md",
                            Sha256 = new string('d', 64),
                        },
                    ],
                },
            },
        };
}
