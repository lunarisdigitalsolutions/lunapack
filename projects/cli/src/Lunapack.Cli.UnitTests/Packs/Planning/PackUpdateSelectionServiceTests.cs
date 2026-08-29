using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using NuGet.Versioning;

namespace Lunapack.Cli.UnitTests.Packs.Planning;

public sealed class PackUpdateSelectionServiceTests
{
    [Test]
    public async Task SelectAvailable_WhenNewerConfiguredPackExists_ReturnsCurrentAndLatest()
    {
        var state = CreateState(new ProjectConfiguration.RequestedPack { Id = "example" }, "1.0.0");
        var catalog = new[]
        {
            CreateCatalogPack("example", "1.1.0", sourceOrder: 1),
            CreateCatalogPack("example", "1.2.0", sourceOrder: 0),
        };

        var result = PackUpdateSelectionService.SelectAvailable(state, catalog);

        var update = result.RequireValue().Single();
        await Assert.That(update.Current.Version).IsEqualTo("1.0.0");
        await Assert.That(update.Latest.Manifest.Version).IsEqualTo("1.2.0");
    }

    [Test]
    public async Task SelectAvailable_WhenEqualVersionFromMultipleSources_DoesNotReportUpdate()
    {
        var state = CreateState(new ProjectConfiguration.RequestedPack { Id = "example" }, "1.0.0");
        var catalog = new[]
        {
            CreateCatalogPack("example", "1.0.0", sourceOrder: 1),
            CreateCatalogPack("example", "1.0.0", sourceOrder: 0),
        };

        var result = PackUpdateSelectionService.SelectAvailable(state, catalog);

        await Assert.That(result.RequireValue()).IsEmpty();
    }

    [Test]
    public async Task SelectAvailable_WhenNoConfiguredCandidateExists_DoesNotReportUpdate()
    {
        var state = CreateState(new ProjectConfiguration.RequestedPack { Id = "example" }, "1.0.0");

        var result = PackUpdateSelectionService.SelectAvailable(state, []);

        await Assert.That(result.RequireValue()).IsEmpty();
    }

    [Test]
    public async Task SelectAvailable_WhenAnotherSourceHasNewerPack_UsesLockedSourceCandidate()
    {
        var state = CreateState(new ProjectConfiguration.RequestedPack { Id = "example" }, "1.0.0");
        state.LockFile.Packs[0].SourceIdentity = ConfiguredSourceIdentity.CreateLocal("first");
        var catalog = new[]
        {
            CreateCatalogPack("example", "1.1.0", sourceOrder: 0, sourcePath: "first"),
            CreateCatalogPack("example", "2.0.0", sourceOrder: 1, sourcePath: "second"),
        };

        var result = PackUpdateSelectionService.SelectAvailable(state, catalog);

        await Assert
            .That(result.RequireValue().Single().Latest.Manifest.Version)
            .IsEqualTo("1.1.0");
    }

    [Test]
    public async Task SelectExplicit_WhenOnlyAnotherSourceHasVersion_ModelsSourceSwitch()
    {
        var currentPack = CreateState(
            new ProjectConfiguration.RequestedPack { Id = "example" },
            "1.0.0"
        )
            .LockFile.Packs.Single();
        currentPack.SourceIdentity = ConfiguredSourceIdentity.CreateLocal("first");
        var catalog = new[] { CreateCatalogPack("example", "2.0.0", 1, "second") };

        var result = LockedSourceUpdateSelector.SelectExplicit(currentPack, catalog, "2.0.0");

        await Assert.That(result.RequireValue().Candidate.Manifest.Version).IsEqualTo("2.0.0");
        await Assert.That(result.RequireValue().SourceSwitch).IsNotNull();
        await Assert
            .That(result.RequireValue().SourceSwitch.RequireNotNull().CurrentSource.Path)
            .IsEqualTo("first");
        await Assert
            .That(result.RequireValue().SourceSwitch.RequireNotNull().SelectedSource.Path)
            .IsEqualTo("second");
    }

    private static ProjectState CreateState(
        ProjectConfiguration.RequestedPack requestedRoot,
        string resolvedVersion
    ) =>
        new()
        {
            Configuration = new ProjectConfiguration { SchemaVersion = 1, Packs = [requestedRoot] },
            LockFile = new ProjectLockFile
            {
                SchemaVersion = 1,
                Packs =
                [
                    new ProjectLockFile.ResolvedPack
                    {
                        Id = requestedRoot.Id,
                        Version = resolvedVersion,
                        SourcePath = "source",
                        PackPath = "example",
                    },
                ],
            },
        };

    private static CatalogPack CreateCatalogPack(
        string id,
        string version,
        int sourceOrder,
        string? sourcePath = null
    ) =>
        new(
            sourcePath ?? $"source-{sourceOrder}",
            $"{sourcePath ?? $"source-{sourceOrder}"}\\{id}",
            sourceOrder,
            new PackManifest
            {
                Id = id,
                Version = version,
                ManagedFiles =
                [
                    new PackManifest.PackManagedFile
                    {
                        Source = "source.txt",
                        Target = "target.txt",
                    },
                ],
            },
            NuGetVersion.Parse(version),
            $"source-{sourceOrder}",
            ConfiguredSourceIdentity.CreateLocal(sourcePath ?? $"source-{sourceOrder}")
        );
}
