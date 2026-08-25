using System.IO.Abstractions.TestingHelpers;

namespace Lunapack.Cli.UnitTests;

public sealed class ManagedFileTargetRemappingTests
{
    [Test]
    public async Task Resolve_WhenFileAndDirectoryMappingsMatch_PrefersFileMapping()
    {
        var remapping = ManagedFileTargetRemapping.FromConfiguration(
            new ProjectConfiguration.Remapping
            {
                Directories = { ["docs/adr"] = "docs/architecture/decisions" },
                Files = { ["docs/adr/template.md"] = "docs/adr/_template.md" },
            }
        );

        var target = remapping.Resolve("docs/adr/template.md");

        await Assert.That(target).IsEqualTo("docs/adr/_template.md");
    }

    [Test]
    public async Task Resolve_WhenInvocationDirectoryMappingMatches_PreservesDescendantSuffix()
    {
        var remapping = ManagedFileTargetRemapping
            .Create(
                new MockFileSystem(),
                "C:\\project",
                ["docs/adr=docs/architecture/decisions"],
                []
            )
            .RequireValue();

        var target = remapping.Resolve("docs/adr/records/template.md");

        await Assert.That(target).IsEqualTo("docs/architecture/decisions/records/template.md");
    }

    [Test]
    public async Task Resolve_WhenInvocationAndGlobalMappingsMatch_PrefersInvocationMapping()
    {
        var invocationRemapping = ManagedFileTargetRemapping
            .Create(
                new MockFileSystem(),
                "C:\\project",
                ["docs/adr=docs/architecture/decisions"],
                []
            )
            .RequireValue();
        var globalRemapping = ManagedFileTargetRemapping.FromConfiguration(
            new ProjectConfiguration.Remapping
            {
                Directories = { ["docs/adr"] = "docs/internal/adr" },
            }
        );

        var target = invocationRemapping.Resolve("docs/adr/template.md", globalRemapping);

        await Assert.That(target).IsEqualTo("docs/architecture/decisions/template.md");
    }

    [Test]
    public async Task Create_WhenMappingEscapesProject_ReturnsFailure()
    {
        var remapping = ManagedFileTargetRemapping.Create(
            new MockFileSystem(),
            "C:\\project",
            ["docs/adr=../outside"],
            []
        );

        await Assert.That(remapping.IsSuccess).IsFalse();
    }
}
