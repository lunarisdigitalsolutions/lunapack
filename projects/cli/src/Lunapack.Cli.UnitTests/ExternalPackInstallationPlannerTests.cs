using System.Text;

namespace Lunapack.Cli.UnitTests;

public sealed class ExternalPackInstallationPlannerTests
{
    [Test]
    public async Task Plan_WhenExternalDirectorySelected_AppliesExclusionsAndProvenance()
    {
        using var workspace = new TestWorkspace();
        var contentRoot = CreateContentRoot(workspace.Path);
        Directory.CreateDirectory(Path.Combine(contentRoot, "docs", "internal"));
        File.WriteAllText(Path.Combine(contentRoot, "docs", "public.md"), "public");
        File.WriteAllText(Path.Combine(contentRoot, "docs", "internal", "private.md"), "private");
        var pack = CreatePack(
            new PackManifest.PackManagedFile
            {
                Source = "upstream",
                Directory = "docs",
                Exclude = ["internal/**"],
                Target = "standards",
            }
        );

        var result = CreatePlanner(workspace)
            .Plan(
                workspace.Path,
                new ResolvedPackGraph([pack]),
                new ProjectLockFile { SchemaVersion = 1 },
                CreateConfiguration(),
                CreateRequest(),
                EmptyParameters(),
                CreateRoots(pack, contentRoot)
            );

        await Assert.That(result.Value?.ManagedFiles.Count).IsEqualTo(1);
        await Assert
            .That(result.Value?.ManagedFiles.Single().TargetPathRelativeToProject)
            .IsEqualTo("standards/public.md");
        await Assert
            .That(result.Value?.ManagedFiles.Single().ExternalSource?.Alias)
            .IsEqualTo("upstream");
        await Assert
            .That(result.Value?.ManagedFiles.Single().Pack.Manifest.Id)
            .IsEqualTo("example");
    }

    [Test]
    public async Task Plan_WhenExternalGlobFlatteningCollides_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        var contentRoot = CreateContentRoot(workspace.Path);
        Directory.CreateDirectory(Path.Combine(contentRoot, "first"));
        Directory.CreateDirectory(Path.Combine(contentRoot, "second"));
        File.WriteAllText(Path.Combine(contentRoot, "first", "README.md"), "first");
        File.WriteAllText(Path.Combine(contentRoot, "second", "README.md"), "second");
        var pack = CreatePack(
            new PackManifest.PackManagedFile
            {
                Source = "upstream",
                Glob = "**/*.md",
                Flatten = true,
                Target = "docs",
            }
        );

        var result = CreatePlanner(workspace)
            .Plan(
                workspace.Path,
                new ResolvedPackGraph([pack]),
                new ProjectLockFile { SchemaVersion = 1 },
                CreateConfiguration(),
                CreateRequest(),
                EmptyParameters(),
                CreateRoots(pack, contentRoot)
            );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("cannot flatten");
    }

    [Test]
    public async Task Plan_WhenExternalTemplateSelected_AppliesConditionTemplateAndRemapping()
    {
        using var workspace = new TestWorkspace();
        var contentRoot = CreateContentRoot(workspace.Path);
        File.WriteAllText(Path.Combine(contentRoot, "template.md"), "Hello {{ name }}");
        var pack = CreatePack(
            new PackManifest.PackManagedFile
            {
                Source = "upstream",
                Path = "template.md",
                Target = "README.md",
                Template = true,
                Condition = "enabled",
            }
        );
        var configuration = CreateConfiguration() with
        {
            Remap = new ProjectConfiguration.Remapping
            {
                Files = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["README.md"] = "docs/README.md",
                },
            },
        };
        var parameters = new ResolvedPackParameters(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal)
            {
                ["enabled"] = new(PackParameterType.Bool, false, []),
                ["name"] = new(PackParameterType.String, false, []),
            },
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
            {
                ["enabled"] = new(PackParameterType.Bool, string.Empty, true),
                ["name"] = new(PackParameterType.String, "Luna", false),
            }
        );

        var result = CreatePlanner(workspace)
            .Plan(
                workspace.Path,
                new ResolvedPackGraph([pack]),
                new ProjectLockFile { SchemaVersion = 1 },
                configuration,
                CreateRequest(),
                parameters,
                CreateRoots(pack, contentRoot)
            );

        await Assert
            .That(result.Value?.ManagedFiles.Single().TargetPathRelativeToProject)
            .IsEqualTo("docs/README.md");
        await Assert
            .That(Encoding.UTF8.GetString(result.Value?.ManagedFiles.Single().Contents ?? []))
            .IsEqualTo("Hello Luna");
    }

    [Test]
    public async Task Plan_WhenMultiSelectMembershipMatches_SelectsAndRendersTemplate()
    {
        using var workspace = new TestWorkspace();
        var contentRoot = CreateContentRoot(workspace.Path);
        File.WriteAllText(
            Path.Combine(contentRoot, "template.md"),
            "{{ if features contains \"docker\" }}Docker{{ end }}"
        );
        var pack = CreatePack(
            new PackManifest.PackManagedFile
            {
                Source = "upstream",
                Path = "template.md",
                Target = "README.md",
                Template = true,
                Condition = "\"docker\" in features",
            }
        );
        var parameters = new ResolvedPackParameters(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal)
            {
                ["features"] = new(
                    PackParameterType.Enum,
                    false,
                    ["api", "docker"],
                    Multiple: true
                ),
            },
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
            {
                ["features"] = new(PackParameterType.Enum, string.Empty, false, ["api", "docker"]),
            }
        );

        var result = CreatePlanner(workspace)
            .Plan(
                workspace.Path,
                new ResolvedPackGraph([pack]),
                new ProjectLockFile { SchemaVersion = 1 },
                CreateConfiguration(),
                CreateRequest(),
                parameters,
                CreateRoots(pack, contentRoot)
            );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert
            .That(Encoding.UTF8.GetString(result.RequireValue().ManagedFiles.Single().Contents))
            .IsEqualTo("Docker");
    }

    private static string CreateContentRoot(string workspace)
    {
        var root = Path.Combine(workspace, "external");
        Directory.CreateDirectory(root);
        return root;
    }

    private static DiscoveredPack CreatePack(PackManifest.PackManagedFile managedFile)
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Author = "Example",
            License = "MIT",
            ManagedFiles = [managedFile],
            Sources = new Dictionary<string, PackManifest.PackSource>(StringComparer.Ordinal)
            {
                ["upstream"] = new()
                {
                    Url = "https://github.com/example/standards.git",
                    Ref = "refs/heads/main",
                },
            },
        };
        return new DiscoveredPack("source", "pack", manifest);
    }

    private static PackInstallationPlanner CreatePlanner(TestWorkspace workspace) =>
        new(
            workspace.FileSystem,
            new PackTemplateRenderer(workspace.FileSystem),
            new ManagedFileConditionParser()
        );

    private static ProjectConfiguration CreateConfiguration() =>
        new()
        {
            SchemaVersion = 1,
            Packs = [new ProjectConfiguration.RequestedPack { Id = "example", Version = "1.0.0" }],
        };

    private static PackInstallationRequest CreateRequest() =>
        new(new PackReference("example", "1.0.0"), null, false);

    private static ResolvedPackParameters EmptyParameters() =>
        new(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal),
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
        );

    private static ExternalContentRoots CreateRoots(DiscoveredPack pack, string directory) =>
        new([
            (
                pack.Manifest.Id,
                new ExternalContentRoot(
                    "upstream",
                    directory,
                    "standards",
                    "git:github.com/example/standards@refs/heads/main#/",
                    "refs/heads/main",
                    "1111111111111111111111111111111111111111"
                )
            ),
        ]);
}
