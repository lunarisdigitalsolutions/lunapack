using Lunapack.Cli.Catalog;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Oci;
using NuGet.Versioning;

namespace Lunapack.Cli.UnitTests.Packs.Planning;

public sealed class OutdatedJsonMapperTests
{
    [Test]
    public async Task Scenario_OutdatedEvidence_MapsEveryStructuredResult()
    {
        var current = CreateResolvedPack("example", "1.0.0", "registry", "locked-digest");
        var latest = CreateCatalogPack("example", "1.1.0", "observed-digest");
        var availability = new PackUpdateAvailability(
            [
                new AvailablePackUpdate(
                    new ProjectConfiguration.RequestedPack { Id = "example" },
                    current,
                    latest,
                    "pack update"
                ),
            ],
            [
                new OciReleaseIntegrityViolation(
                    current,
                    CreateCatalogPack("example", "1.0.0", "replacement-digest")
                ),
            ],
            [CreateResolvedPack("unchecked", "2.0.0", null, "unchecked-digest")]
        );
        var links = new LinkOutdatedReport[] { new("link", ["target changed", "ref changed"]) };

        var result = OutdatedJsonMapper.Map(availability, links, remoteRefsChecked: false);

        await Assert.That(result.RemoteRefsChecked).IsFalse();
        await Assert.That(result.Updates.Single().Id).IsEqualTo("example");
        await Assert.That(result.Updates.Single().CurrentVersion).IsEqualTo("1.0.0");
        await Assert.That(result.Updates.Single().LatestVersion).IsEqualTo("1.1.0");
        await Assert
            .That(result.OutdatedLinks.Single().Reasons)
            .IsEquivalentTo(["ref changed", "target changed"]);
        await Assert
            .That(result.IntegrityViolations.Single().LockedManifestDigest)
            .IsEqualTo("locked-digest");
        await Assert
            .That(result.IntegrityViolations.Single().ObservedManifestDigest)
            .IsEqualTo("replacement-digest");
        await Assert.That(result.UncheckedOciPacks.Single().Id).IsEqualTo("unchecked");
        await Assert.That(result.UncheckedOciPacks.Single().SourceName).IsNull();
    }

    private static CatalogPack CreateCatalogPack(
        string id,
        string version,
        string manifestDigest
    ) =>
        new(
            "source",
            id,
            0,
            new PackManifest { Id = id, Version = version },
            NuGetVersion.Parse(version),
            "registry",
            ConfiguredSourceIdentity.CreateOci("https://registry.example.test", "packs"),
            OciSource: CreateOciProvenance(manifestDigest)
        );

    private static ProjectLockFile.ResolvedPack CreateResolvedPack(
        string id,
        string version,
        string? sourceName,
        string manifestDigest
    ) =>
        new()
        {
            Id = id,
            Version = version,
            PackPath = id,
            SourceName = sourceName,
            OciSource = CreateOciProvenance(manifestDigest),
        };

    private static OciSourceProvenance CreateOciProvenance(string manifestDigest) =>
        new()
        {
            RegistryUrl = "https://registry.example.test",
            Repository = "packs",
            Tag = "1.0.0",
            ManifestDigest = manifestDigest,
            ContentDigest = "content-digest",
            ContentSize = 1,
        };
}
