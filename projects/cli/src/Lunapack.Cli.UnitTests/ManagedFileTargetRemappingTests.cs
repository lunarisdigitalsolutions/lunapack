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
    [Arguments(
        "docs/development/",
        "docs/04-development/",
        "docs/development",
        "docs/04-development"
    )]
    [Arguments(
        "docs\\development\\",
        "docs\\04-development\\",
        "docs\\development\\nested\\guide.md",
        "docs/04-development/nested/guide.md"
    )]
    [Arguments(
        "docs/development",
        "docs/04-development",
        "docs/development/nested/deeper/guide.md",
        "docs/04-development/nested/deeper/guide.md"
    )]
    public async Task Resolve_WhenConfiguredDirectoryPathsVary_NormalizesAndPreservesSuffix(
        string source,
        string destination,
        string declaredTarget,
        string expectedTarget
    )
    {
        var remapping = ManagedFileTargetRemapping.FromConfiguration(
            new ProjectConfiguration.Remapping { Directories = { [source] = destination } }
        );

        var target = remapping.Resolve(declaredTarget);

        await Assert.That(target).IsEqualTo(expectedTarget);
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
    public async Task Resolve_WhenInvocationDirectoryAndConfiguredFileMatch_PrefersFileMapping()
    {
        var invocationRemapping = ManagedFileTargetRemapping
            .Create(new MockFileSystem(), "C:\\project", ["docs/adr=docs/invocation"], [])
            .RequireValue();
        var configuredRemapping = ManagedFileTargetRemapping.FromConfiguration(
            new ProjectConfiguration.Remapping
            {
                Files = { ["docs/adr/template.md"] = "docs/configured/template.md" },
            }
        );

        var target = invocationRemapping.Resolve("docs/adr/template.md", configuredRemapping);

        await Assert.That(target).IsEqualTo("docs/configured/template.md");
    }

    [Test]
    public async Task Resolve_WhenDirectoryMapsToIgnore_ReturnsIgnoreForDescendant()
    {
        var remapping = ManagedFileTargetRemapping
            .Create(new MockFileSystem(), "C:\\project", ["docs/development=@ignore"], [])
            .RequireValue();

        var target = remapping.Resolve("docs/development/nested/guide.md");

        await Assert.That(target).IsEqualTo(ManagedFileTargetRemapping.IgnoreTarget);
    }

    [Test]
    public async Task Resolve_WhenDirectoryIgnoredAndFileRemapped_UsesFileException()
    {
        var remapping = ManagedFileTargetRemapping.FromConfiguration(
            new ProjectConfiguration.Remapping
            {
                Directories = { ["docs/development"] = "@ignore" },
                Files = { ["docs/development/keep.md"] = "docs/selected/keep.md" },
            }
        );

        var target = remapping.Resolve("docs/development/keep.md");

        await Assert.That(target).IsEqualTo("docs/selected/keep.md");
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
