using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.UnitTests.Sources.Oci;

public sealed class OciRepositoryTagMapperTests
{
    [Test]
    public async Task Scenario_CreateRepository_CombinesCanonicalSourcePathAndPackId()
    {
        var source = new ProjectConfiguration.OciSource
        {
            Name = "customer",
            Url = "https://registry.example.test",
            Path = "customers/acme",
        };

        var repository = OciRepositoryTagMapper.CreateRepository(source, "backend");

        await Assert.That(repository.Value).IsEqualTo("customers/acme/backend");
    }

    [Test]
    public async Task Scenario_CreateRepository_OmitsEmptySourcePath()
    {
        var source = new ProjectConfiguration.OciSource
        {
            Name = "public",
            Url = "https://registry.example.test",
        };

        var repository = OciRepositoryTagMapper.CreateRepository(source, "backend");

        await Assert.That(repository.Value).IsEqualTo("backend");
    }

    [Test]
    [Arguments("1.4.0", "1.4.0")]
    [Arguments("2.0.0-rc.1", "2.0.0-rc.1")]
    [Arguments("1.4.0+build.5", "1.4.0_build.5")]
    [Arguments("1.4.0-rc.1+build.5", "1.4.0-rc.1_build.5")]
    public async Task Scenario_CreateTag_PreservesVersionThroughReversibleMapping(
        string version,
        string expectedTag
    )
    {
        var tag = OciRepositoryTagMapper.CreateTag(version);
        var decoded = OciRepositoryTagMapper.ReadVersion(expectedTag);

        await Assert.That(tag.Value).IsEqualTo(expectedTag);
        await Assert.That(decoded.Value).IsEqualTo(version);
    }

    [Test]
    public async Task Scenario_ReadPackId_RejectsSiblingPathAndNestedRepository()
    {
        var sibling = OciRepositoryTagMapper.ReadPackId("packs-old/backend", "packs");
        var nested = OciRepositoryTagMapper.ReadPackId("packs/category/backend", "packs");

        await Assert.That(sibling.IsSuccess).IsFalse();
        await Assert.That(nested.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Scenario_CreateRepository_RejectsInvalidPackIdWithoutChangingIt()
    {
        var source = new ProjectConfiguration.OciSource
        {
            Name = "private",
            Url = "https://registry.example.test",
        };

        var repository = OciRepositoryTagMapper.CreateRepository(source, "Backend");

        await Assert.That(repository.IsSuccess).IsFalse();
    }
}
