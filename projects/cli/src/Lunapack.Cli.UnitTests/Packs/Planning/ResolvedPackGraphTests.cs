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
