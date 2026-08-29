using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests;

public sealed class LinkSelectionTests
{
    private static readonly string[] Candidates =
    [
        "agents/CSharpExpert.agent.md",
        "agents/ai-team-architect.agent.md",
        "agents/ai-team-reviewer.agent.md",
        "agents/nested/deep.agent.md",
        "chatmodes/plan.chatmode.md",
        "README.md",
    ];

    [Test]
    public async Task Select_WhenIncludeNamesSingleFile_ReturnsThatFile()
    {
        var selection = LinkSelectionService.Select(
            "agents-csharp-expert",
            CreateLink(path: "agents", includes: ["CSharpExpert.agent.md"]),
            Candidates
        );

        await Assert
            .That(selection.RequireValue())
            .IsEquivalentTo(["agents/CSharpExpert.agent.md"]);
    }

    [Test]
    public async Task Select_WhenIncludeNamesDirectory_ExpandsRecursively()
    {
        var selection = LinkSelectionService.Select(
            "agents",
            CreateLink(includes: ["agents"]),
            Candidates
        );

        await Assert
            .That(selection.RequireValue())
            .IsEquivalentTo([
                "agents/CSharpExpert.agent.md",
                "agents/ai-team-architect.agent.md",
                "agents/ai-team-reviewer.agent.md",
                "agents/nested/deep.agent.md",
            ]);
    }

    [Test]
    public async Task Select_WhenIncludesOverlap_DeduplicatesAndOrdersOrdinally()
    {
        var selection = LinkSelectionService.Select(
            "agents-ai-team",
            CreateLink(
                path: "agents",
                includes: ["ai-team*.agent.md", "ai-team-architect.agent.md"]
            ),
            Candidates
        );

        await Assert
            .That(selection.RequireValue())
            .IsEquivalentTo([
                "agents/ai-team-architect.agent.md",
                "agents/ai-team-reviewer.agent.md",
            ]);
    }

    [Test]
    public async Task Select_WhenExcludeMatchesIncludedFile_RemovesItAfterUnion()
    {
        var selection = LinkSelectionService.Select(
            "agents",
            CreateLink(path: "agents", includes: ["**/*.agent.md"], excludes: ["nested/*"]),
            Candidates
        );

        await Assert
            .That(selection.RequireValue())
            .IsEquivalentTo([
                "agents/CSharpExpert.agent.md",
                "agents/ai-team-architect.agent.md",
                "agents/ai-team-reviewer.agent.md",
            ]);
    }

    [Test]
    public async Task Select_WhenBasePathIsSet_IgnoresFilesOutsideIt()
    {
        var selection = LinkSelectionService.Select(
            "chatmodes",
            CreateLink(path: "chatmodes", includes: ["**/*.md"]),
            Candidates
        );

        await Assert.That(selection.RequireValue()).IsEquivalentTo(["chatmodes/plan.chatmode.md"]);
    }

    [Test]
    public async Task Select_WhenIncludeMatchesNothing_Fails()
    {
        var selection = LinkSelectionService.Select(
            "agents",
            CreateLink(path: "agents", includes: ["missing.agent.md"]),
            Candidates
        );

        await Assert.That(selection.Error).Contains("does not match any source file");
    }

    [Test]
    public async Task Select_WhenExcludesRemoveEverything_Fails()
    {
        var selection = LinkSelectionService.Select(
            "agents",
            CreateLink(path: "agents", includes: ["**/*.agent.md"], excludes: ["**/*.agent.md"]),
            Candidates
        );

        await Assert.That(selection.Error).Contains("select no files");
    }

    [Test]
    public async Task Select_WhenIncludeUsesWindowsSeparators_MatchesNormalizedPaths()
    {
        var selection = LinkSelectionService.Select(
            "agents",
            CreateLink(includes: [@"agents\nested\deep.agent.md"]),
            Candidates
        );

        await Assert.That(selection.RequireValue()).IsEquivalentTo(["agents/nested/deep.agent.md"]);
    }

    [Test]
    public async Task Map_WhenTargetIsConfigured_PrependsItToRelativePaths()
    {
        var mapper = new LinkTargetMapper(new MockFileSystem());

        var mappings = mapper
            .Map(
                @"C:\project",
                "agents",
                CreateLink(path: "agents", includes: ["**/*"], target: ".github/agents"),
                ["agents/CSharpExpert.agent.md", "agents/nested/deep.agent.md"]
            )
            .RequireValue();

        await Assert
            .That(mappings.Select(mapping => mapping.TargetPath))
            .IsEquivalentTo([
                ".github/agents/CSharpExpert.agent.md",
                ".github/agents/nested/deep.agent.md",
            ]);
    }

    [Test]
    public async Task Map_WhenTargetIsOmitted_UsesWorkspaceRootRelativePaths()
    {
        var mapper = new LinkTargetMapper(new MockFileSystem());

        var mappings = mapper
            .Map(
                @"C:\project",
                "agents",
                CreateLink(includes: ["**/*"]),
                ["agents/CSharpExpert.agent.md"]
            )
            .RequireValue();

        await Assert.That(mappings.Single().TargetPath).IsEqualTo("agents/CSharpExpert.agent.md");
    }

    [Test]
    public async Task Map_WhenDirectoryRemappingSpecified_WritesRemappedEffectiveTarget()
    {
        var fileSystem = new MockFileSystem();
        var mapper = new LinkTargetMapper(fileSystem);
        var remapping = ManagedFileTargetRemapping
            .Create(fileSystem, @"C:\project", ["agents/=.github/agents"], [])
            .RequireValue();

        var mapping = mapper
            .Map(
                @"C:\project",
                "agents",
                CreateLink(includes: ["**/*"]),
                ["agents/CSharpExpert.agent.md"],
                remapping
            )
            .RequireValue()
            .Single();

        await Assert.That(mapping.DeclaredTargetPath).IsEqualTo("agents/CSharpExpert.agent.md");
        await Assert.That(mapping.TargetPath).IsEqualTo(".github/agents/CSharpExpert.agent.md");
    }

    [Test]
    public async Task Map_WhenDirectoryMapsToIgnore_OmitsMatchingFiles()
    {
        var fileSystem = new MockFileSystem();
        var mapper = new LinkTargetMapper(fileSystem);
        var remapping = ManagedFileTargetRemapping
            .Create(fileSystem, @"C:\project", ["agents/=@ignore"], [])
            .RequireValue();

        var mappings = mapper
            .Map(
                @"C:\project",
                "agents",
                CreateLink(includes: ["**/*"]),
                ["agents/CSharpExpert.agent.md", "agents/nested/deep.agent.md"],
                remapping
            )
            .RequireValue();

        await Assert.That(mappings).IsEmpty();
    }

    [Test]
    public async Task Map_WhenStripPrefixIsConfigured_RemovesIt()
    {
        var mapper = new LinkTargetMapper(new MockFileSystem());

        var mappings = mapper
            .Map(
                @"C:\project",
                "agents",
                CreateLink(includes: ["**/*"], stripPrefix: "agents", target: ".github/agents"),
                ["agents/nested/deep.agent.md"]
            )
            .RequireValue();

        await Assert
            .That(mappings.Single().TargetPath)
            .IsEqualTo(".github/agents/nested/deep.agent.md");
    }

    [Test]
    public async Task Map_WhenStripPrefixIsPartialSegment_Fails()
    {
        var mapper = new LinkTargetMapper(new MockFileSystem());

        var mappings = mapper.Map(
            @"C:\project",
            "agents",
            CreateLink(includes: ["**/*"], stripPrefix: "age"),
            ["agents/nested/deep.agent.md"]
        );

        await Assert.That(mappings.Error).Contains("is not a complete prefix");
    }

    [Test]
    public async Task Map_WhenFlattenIsEnabled_UsesFileNamesOnly()
    {
        var mapper = new LinkTargetMapper(new MockFileSystem());

        var mappings = mapper
            .Map(
                @"C:\project",
                "agents",
                CreateLink(includes: ["**/*"], flatten: true, target: ".github/agents"),
                ["agents/nested/deep.agent.md"]
            )
            .RequireValue();

        await Assert.That(mappings.Single().TargetPath).IsEqualTo(".github/agents/deep.agent.md");
    }

    [Test]
    public async Task Map_WhenFlattenCollides_ReportsDuplicateTarget()
    {
        var mapper = new LinkTargetMapper(new MockFileSystem());

        var mappings = mapper.Map(
            @"C:\project",
            "agents",
            CreateLink(includes: ["**/*"], flatten: true),
            ["agents/deep.agent.md", "agents/nested/deep.agent.md"]
        );

        await Assert.That(mappings.Error).Contains("both map to 'deep.agent.md'");
    }

    [Test]
    public async Task Map_WhenTargetEscapesProject_Fails()
    {
        var mapper = new LinkTargetMapper(new MockFileSystem());

        var mappings = mapper.Map(
            @"C:\project",
            "agents",
            CreateLink(includes: ["**/*"], target: "../outside"),
            ["agents/deep.agent.md"]
        );

        await Assert.That(mappings.Error).Contains("Link 'agents'");
    }

    private static ProjectConfiguration.Link CreateLink(
        string? path = null,
        IReadOnlyList<string>? includes = null,
        IReadOnlyList<string>? excludes = null,
        string? target = null,
        string? stripPrefix = null,
        bool? flatten = null
    ) =>
        new()
        {
            Excludes = [.. excludes ?? []],
            Flatten = flatten,
            Includes = [.. includes ?? []],
            Path = path,
            Source = "awesome-copilot",
            StripPrefix = stripPrefix,
            Target = target,
        };
}
