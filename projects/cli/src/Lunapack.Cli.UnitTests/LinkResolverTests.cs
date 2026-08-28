using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace Lunapack.Cli.UnitTests;

public sealed class LinkResolverTests
{
    private static readonly string ProjectDirectory = OperatingSystem.IsWindows()
        ? @"C:\project"
        : "/project";

    [Test]
    public async Task ResolveAsync_WhenLocalSourceIsSelected_EmitsManagedRootAndLockRecord()
    {
        var fileSystem = CreateFileSystem();

        using var resolution = (await ResolveAsync(fileSystem, CreateLink())).RequireValue();

        var snapshot = resolution.Snapshot;
        await Assert.That(snapshot.SourceName).IsEqualTo("upstream");
        await Assert.That(snapshot.SourceIdentity.Type).IsEqualTo("local");
        await Assert
            .That(snapshot.Files.Select(file => file.TargetPath))
            .IsEquivalentTo([
                ".github/agents/CSharpExpert.agent.md",
                ".github/agents/ai-team.agent.md",
            ]);
        await Assert
            .That(snapshot.ToManagedRoot().Owner)
            .IsEqualTo(new ManagedRootOwner(ManagedRootKind.Link, "agents"));
        await Assert
            .That(snapshot.ToLockRecord().DefinitionSha256)
            .IsEqualTo(snapshot.DefinitionSha256);
    }

    [Test]
    public async Task ResolveAsync_WhenFilesAreSelected_HashesSnapshotContents()
    {
        var fileSystem = CreateFileSystem();

        using var resolution = (await ResolveAsync(fileSystem, CreateLink())).RequireValue();

        var file = resolution.Snapshot.Files.Single(candidate =>
            string.Equals(
                candidate.SourcePath,
                "agents/CSharpExpert.agent.md",
                StringComparison.Ordinal
            )
        );
        await Assert
            .That(file.Sha256)
            .IsEqualTo("C7D253870AB8DE3825E3A9B5EE603E21ABD0DFE62763E8E2FC1FC9F4684E8A19");
    }

    [Test]
    public async Task ResolveAsync_WhenSourceMutatesAfterSnapshot_UsesSnapshotContents()
    {
        var fileSystem = CreateFileSystem();

        using var resolution = (await ResolveAsync(fileSystem, CreateLink())).RequireValue();
        fileSystem.File.WriteAllText(SourceFilePath("agents", "CSharpExpert.agent.md"), "mutated");

        var file = resolution.Snapshot.Files[0];
        await Assert
            .That(Encoding.UTF8.GetString(resolution.ReadContents(file)))
            .IsEqualTo("expert");
    }

    [Test]
    public async Task Dispose_WhenResolutionCompletes_RemovesSnapshotWorkspace()
    {
        var fileSystem = CreateFileSystem();
        var resolution = (await ResolveAsync(fileSystem, CreateLink())).RequireValue();
        var snapshotPath = resolution.Snapshot.Files[0].SnapshotPath;

        resolution.Dispose();

        await Assert.That(fileSystem.File.Exists(snapshotPath)).IsFalse();
    }

    [Test]
    public async Task ResolveAsync_WhenSourceIsNotConfigured_Fails()
    {
        var fileSystem = CreateFileSystem();
        var link = CreateLink();
        link.Source = "missing";

        var resolution = await ResolveAsync(fileSystem, link);

        await Assert.That(resolution.Error).Contains("which is not configured");
    }

    [Test]
    public async Task ResolveAsync_WhenSourceNameCasingDiffers_Fails()
    {
        var fileSystem = CreateFileSystem();
        var link = CreateLink();
        link.Source = "Upstream";

        var resolution = await ResolveAsync(fileSystem, link);

        await Assert.That(resolution.Error).Contains("which is not configured");
    }

    [Test]
    public async Task ResolveAsync_WhenLocalLinkDeclaresRef_Fails()
    {
        var fileSystem = CreateFileSystem();
        var link = CreateLink();
        link.Ref = "main";

        var resolution = await ResolveAsync(fileSystem, link);

        await Assert.That(resolution.Error).Contains("does not support refs");
    }

    [Test]
    public async Task ResolveAsync_WhenSourceDirectoryIsMissing_Fails()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(ProjectDirectory);

        var resolution = await ResolveAsync(fileSystem, CreateLink());

        await Assert.That(resolution.Error).Contains("does not exist");
    }

    [Test]
    public async Task ResolveAsync_WhenSourceContainsSymbolicLink_SkipsIt()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.File.CreateSymbolicLink(
            SourceFilePath("agents", "linked.agent.md"),
            fileSystem.Path.Combine(ProjectDirectory, "secret.md")
        );

        using var resolution = (await ResolveAsync(fileSystem, CreateLink())).RequireValue();

        await Assert
            .That(resolution.Snapshot.Files.Select(file => file.SourcePath))
            .IsEquivalentTo(["agents/CSharpExpert.agent.md", "agents/ai-team.agent.md"]);
    }

    [Test]
    public async Task ResolveAsync_WhenProjectRemappingConfigured_MapsSelectedTargets()
    {
        var fileSystem = CreateFileSystem();
        var configuration = CreateConfiguration();
        configuration.Remap = new ProjectConfiguration.Remapping
        {
            Directories = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".github/agents"] = ".config/agents",
            },
        };

        using var resolution = (
            await ResolveAsync(fileSystem, CreateLink(), configuration)
        ).RequireValue();

        await Assert
            .That(resolution.Snapshot.Files.Select(file => file.TargetPath))
            .IsEquivalentTo([
                ".config/agents/CSharpExpert.agent.md",
                ".config/agents/ai-team.agent.md",
            ]);
    }

    [Test]
    public async Task ResolveAsync_WhenInvocationAndProjectRemappingsOverlap_UsesInvocationMapping()
    {
        var fileSystem = CreateFileSystem();
        var configuration = CreateConfiguration();
        configuration.Remap = new ProjectConfiguration.Remapping
        {
            Directories = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".github/agents"] = ".config/agents",
            },
        };
        var invocationRemapping = ManagedFileTargetRemapping
            .Create(fileSystem, ProjectDirectory, [".github/agents=.invocation/agents"], [])
            .RequireValue();

        using var resolution = (
            await ResolveAsync(fileSystem, CreateLink(), configuration, invocationRemapping)
        ).RequireValue();

        await Assert
            .That(resolution.Snapshot.Files.Select(file => file.TargetPath))
            .IsEquivalentTo([
                ".invocation/agents/CSharpExpert.agent.md",
                ".invocation/agents/ai-team.agent.md",
            ]);
    }

    [Test]
    public async Task ResolveAsync_WhenProjectRemappingChanges_RetainsLockedTarget()
    {
        var fileSystem = CreateFileSystem();
        var configuration = CreateConfiguration();
        configuration.Remap = new ProjectConfiguration.Remapping
        {
            Directories = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".github/agents"] = ".new/agents",
            },
        };
        var retainedTargets = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agents/CSharpExpert.agent.md"] = ".retained/expert.agent.md",
        };

        using var resolution = (
            await ResolveAsync(
                fileSystem,
                CreateLink(),
                configuration,
                retainedTargets: retainedTargets
            )
        ).RequireValue();

        await Assert
            .That(resolution.Snapshot.Files.Select(file => file.TargetPath))
            .IsEquivalentTo([".retained/expert.agent.md", ".new/agents/ai-team.agent.md"]);
    }

    private static Task<ManifestOperationResult<LinkResolution>> ResolveAsync(
        MockFileSystem fileSystem,
        ProjectConfiguration.Link link,
        ProjectConfiguration? configuration = null,
        ManagedFileTargetRemapping? targetRemapping = null,
        IReadOnlyDictionary<string, string>? retainedTargets = null
    )
    {
        var resolver = new LinkResolver(
            fileSystem,
            new LinkTargetMapper(fileSystem),
            [new LocalLinkSourceProvider(fileSystem)]
        );
        return resolver.ResolveAsync(
            ProjectDirectory,
            configuration ?? CreateConfiguration(),
            "agents",
            link,
            targetRemapping: targetRemapping,
            retainedTargets: retainedTargets
        );
    }

    private static ProjectConfiguration CreateConfiguration() =>
        new()
        {
            Sources =
            [
                new ProjectConfiguration.LocalSource { Name = "upstream", Path = "upstream" },
            ],
        };

    private static MockFileSystem CreateFileSystem()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(
            SourceFilePath("agents", "CSharpExpert.agent.md"),
            new MockFileData("expert")
        );
        fileSystem.AddFile(SourceFilePath("agents", "ai-team.agent.md"), new MockFileData("team"));
        fileSystem.AddFile(SourceFilePath("README.md"), new MockFileData("readme"));
        fileSystem.AddFile(Path.Combine(ProjectDirectory, "secret.md"), new MockFileData("secret"));
        return fileSystem;
    }

    private static string SourceFilePath(params string[] segments) =>
        Path.Combine([ProjectDirectory, "upstream", .. segments]);

    private static ProjectConfiguration.Link CreateLink() =>
        new()
        {
            Includes = ["**/*.agent.md"],
            Path = "agents",
            Source = "upstream",
            Target = ".github/agents",
        };
}
