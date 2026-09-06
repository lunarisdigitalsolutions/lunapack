using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;

namespace Lunapack.Cli.UnitTests.Packs.Planning;

public sealed class ResolvedPackGraphTests
{
    [Test]
    public async Task Select_WhenReferenceConditionIsFalse_OmitsReferencedPack()
    {
        var dependency = CreatePack("dependency");
        var root = CreatePack("root");
        root.Manifest.Packs.Add(
            new PackManifest.PackReference
            {
                Id = "dependency",
                Version = "1.0.0",
                Condition = "includeDependency",
            }
        );
        var parameters = new ResolvedPackParameters(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal)
            {
                ["includeDependency"] = new(PackParameterType.Bool, false, []),
            },
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
            {
                ["includeDependency"] = new(PackParameterType.Bool, string.Empty, false),
            }
        );

        var result = new ResolvedPackGraph(
            [dependency, root],
            new HashSet<string>(["root"], StringComparer.Ordinal)
        ).Select(parameters);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Packs.Select(pack => pack.Manifest.Id))
            .IsEquivalentTo(["root"]);
    }

    [Test]
    public async Task ResolveCompositeTarget_WhenNestedReferenceRemapsTarget_UsesNearestMapping()
    {
        var dependency = CreatePack("dependency");
        var middle = CreatePack("middle");
        middle.Manifest.Packs.Add(CreateReference("dependency", "docs", "middle-docs"));
        var root = CreatePack("root");
        root.Manifest.Packs.Add(CreateReference("middle", "docs", "root-docs"));
        var graph = new ResolvedPackGraph(
            [dependency, middle, root],
            new HashSet<string>(["root"], StringComparer.Ordinal)
        );

        var result = graph.ResolveCompositeTarget(dependency, "docs/guide.md");

        await Assert.That(result.IsSuccess).IsTrue();
        var resolution = result.Value ?? throw new InvalidOperationException();
        await Assert.That(resolution.EffectiveTarget).IsEqualTo("middle-docs/guide.md");
        await Assert.That(resolution.ParentPackId).IsEqualTo("middle");
    }

    [Test]
    public async Task ResolveCompositeTarget_WhenSameDepthMappingsConflict_ReturnsFailure()
    {
        var dependency = CreatePack("dependency");
        var first = CreatePack("first");
        first.Manifest.Packs.Add(CreateReference("dependency", "docs", "first-docs"));
        var second = CreatePack("second");
        second.Manifest.Packs.Add(CreateReference("dependency", "docs", "second-docs"));
        var root = CreatePack("root");
        root.Manifest.Packs.Add(new PackManifest.PackReference { Id = "first", Version = "1.0.0" });
        root.Manifest.Packs.Add(
            new PackManifest.PackReference { Id = "second", Version = "1.0.0" }
        );
        var graph = new ResolvedPackGraph(
            [dependency, first, second, root],
            new HashSet<string>(["root"], StringComparer.Ordinal)
        );

        var result = graph.ResolveCompositeTarget(dependency, "docs/guide.md");

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task ResolveCompositeTarget_WhenDiamondMappingsEquivalent_ReturnsMapping()
    {
        var dependency = CreatePack("dependency");
        var first = CreatePack("first");
        first.Manifest.Packs.Add(CreateReference("dependency", "docs", "shared-docs"));
        var second = CreatePack("second");
        second.Manifest.Packs.Add(CreateReference("dependency", "docs", "shared-docs"));
        var root = CreatePack("root");
        root.Manifest.Packs.Add(new PackManifest.PackReference { Id = "first", Version = "1.0.0" });
        root.Manifest.Packs.Add(
            new PackManifest.PackReference { Id = "second", Version = "1.0.0" }
        );
        var graph = new ResolvedPackGraph(
            [dependency, first, second, root],
            new HashSet<string>(["root"], StringComparer.Ordinal)
        );

        var result = graph.ResolveCompositeTarget(dependency, "docs/guide.md");

        await Assert.That(result.IsSuccess).IsTrue();
        var resolution = result.Value ?? throw new InvalidOperationException();
        await Assert.That(resolution.EffectiveTarget).IsEqualTo("shared-docs/guide.md");
    }

    [Test]
    public async Task ResolveCompositeTarget_WhenRemappedReferenceInactive_ReturnsNoMapping()
    {
        var dependency = CreatePack("dependency");
        var root = CreatePack("root");
        root.Manifest.Packs.Add(
            CreateReference("dependency", "docs", "inactive-docs") with
            {
                Condition = "includeDependency",
            }
        );
        var parameters = new ResolvedPackParameters(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal)
            {
                ["includeDependency"] = new(PackParameterType.Bool, false, []),
            },
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
            {
                ["includeDependency"] = new(PackParameterType.Bool, string.Empty, false),
            }
        );
        var graph = new ResolvedPackGraph(
            [dependency, root],
            new HashSet<string>(["root"], StringComparer.Ordinal)
        )
            .Select(parameters)
            .RequireValue();

        var result = graph.ResolveCompositeTarget(dependency, "docs/guide.md");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsNull();
    }

    private static PackManifest.PackReference CreateReference(
        string id,
        string source,
        string target
    ) =>
        new()
        {
            Id = id,
            Version = "1.0.0",
            Remap = new PackManifest.PackRemapping { Directories = { [source] = target } },
        };

    private static DiscoveredPack CreatePack(string id) =>
        new(
            "source",
            id,
            new PackManifest
            {
                Id = id,
                Version = "1.0.0",
                Author = "Example Author",
                License = "MIT",
            }
        );
}
