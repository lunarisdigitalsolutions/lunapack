using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests;

public sealed class ExternalSourceRequirementPlannerTests
{
    [Test]
    public async Task PlanAsync_WhenTransitiveRequirementsEquivalent_GroupsAndUsesRootAlias()
    {
        var graph = new ResolvedPackGraph(
            [
                CreatePack("dependency", "shared", "git@github.com:Example/Standards.git"),
                CreatePack("root", "upstream", "https://github.com/example/standards.git"),
            ],
            new HashSet<string>(["root"], StringComparer.Ordinal)
        );

        var result = await CreatePlanner()
            .PlanAsync(graph, new ProjectConfiguration(), EmptyParameters());

        await Assert.That(result.Value?.Groups.Count).IsEqualTo(1);
        await Assert
            .That(result.Value?.Proposed.Single().WorkspaceSourceName)
            .IsEqualTo("upstream");
        await Assert.That(result.Value?.Proposed.Single().FileEntryCount).IsEqualTo(2);
        await Assert.That(result.Value?.Mappings.Count).IsEqualTo(2);
    }

    [Test]
    public async Task PlanAsync_WhenWorkspaceFingerprintExists_ReusesAuthoritativeIdentifier()
    {
        var graph = new ResolvedPackGraph([CreatePack("root", "upstream")]);
        var configuration = new ProjectConfiguration
        {
            Sources =
            [
                new ProjectConfiguration.GitSource
                {
                    Name = "standards",
                    Url = "git@github.com:example/standards.git",
                    Ref = "refs/heads/main",
                },
            ],
        };

        var result = await CreatePlanner().PlanAsync(graph, configuration, EmptyParameters());

        await Assert.That(result.Value?.Proposed).IsEmpty();
        await Assert
            .That(result.Value?.Mappings.Single().WorkspaceSourceName)
            .IsEqualTo("standards");
    }

    [Test]
    public async Task PlanAsync_WhenSameAliasHasDifferentFingerprints_KeepsPackScopeAndReportsConflict()
    {
        var graph = new ResolvedPackGraph(
            [
                CreatePack("first", "upstream", "https://github.com/example/one.git"),
                CreatePack("second", "upstream", "https://github.com/example/two.git"),
            ],
            new HashSet<string>(["first", "second"], StringComparer.Ordinal)
        );

        var result = await CreatePlanner()
            .PlanAsync(graph, new ProjectConfiguration(), EmptyParameters());

        await Assert.That(result.Value?.Groups.Count).IsEqualTo(2);
        await Assert
            .That(result.Value?.Mappings.Select(mapping => mapping.PackId))
            .IsEquivalentTo(["first", "second"]);
        await Assert.That(result.Value?.HasIdentifierConflicts).IsTrue();
    }

    [Test]
    public async Task PlanAsync_WhenConditionalEntryIsNotSelected_IgnoresItsDeclaration()
    {
        var pack = CreatePack("root", "used");
        pack.Manifest.Sources["unused"] = CreateSource("https://github.com/example/unused.git");
        pack.Manifest.ManagedFiles.Add(
            new PackManifest.PackManagedFile
            {
                Source = "unused",
                Path = "disabled.md",
                Target = "disabled.md",
                Condition = "enabled",
            }
        );
        var parameters = new ResolvedPackParameters(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal)
            {
                ["enabled"] = new(PackParameterType.Bool, false, []),
            },
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
            {
                ["enabled"] = new(PackParameterType.Bool, string.Empty, false),
            }
        );

        var result = await CreatePlanner()
            .PlanAsync(new ResolvedPackGraph([pack]), new ProjectConfiguration(), parameters);

        await Assert.That(result.Value?.Groups.Count).IsEqualTo(1);
        await Assert.That(result.Value?.Mappings.Single().Alias).IsEqualTo("used");
    }

    [Test]
    public async Task PlanAsync_WhenProposedIdentifierConfiguredForOtherFingerprint_ReportsConflict()
    {
        var configuration = new ProjectConfiguration
        {
            Sources = [new ProjectConfiguration.LocalSource { Name = "upstream", Path = "packs" }],
        };

        var result = await CreatePlanner()
            .PlanAsync(
                new ResolvedPackGraph([CreatePack("root", "upstream")]),
                configuration,
                EmptyParameters()
            );

        await Assert.That(result.Value?.Proposed.Single().IdentifierConflict).IsNotNull();
    }

    private static ExternalSourceRequirementPlanner CreatePlanner() =>
        new(new GitRefResolver(new StubGitProcessRunner()));

    private static ResolvedPackParameters EmptyParameters() =>
        new(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal),
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
        );

    private static DiscoveredPack CreatePack(
        string id,
        string alias,
        string url = "https://github.com/example/standards.git"
    )
    {
        var manifest = new PackManifest
        {
            Id = id,
            Version = "1.0.0",
            Author = "Example",
            License = "MIT",
            Sources = new Dictionary<string, PackManifest.PackSource>(StringComparer.Ordinal)
            {
                [alias] = CreateSource(url),
            },
            ManagedFiles =
            [
                new PackManifest.PackManagedFile
                {
                    Source = alias,
                    Path = "README.md",
                    Target = $"{id}.md",
                },
            ],
        };
        return new DiscoveredPack(id, id, manifest);
    }

    private static PackManifest.PackSource CreateSource(string url) =>
        new()
        {
            Type = "git",
            Url = url,
            Ref = "main",
        };
}
