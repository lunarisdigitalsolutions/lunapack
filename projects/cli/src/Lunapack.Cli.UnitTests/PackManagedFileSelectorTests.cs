using System.Text.Json;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.UnitTests;

public sealed class PackManagedFileSelectorTests
{
    [Test]
    public async Task Selector_WhenLegacySourceOnly_ResolvesDirectFile()
    {
        var selector = PackManagedFileSelector.Create(
            new PackManifest.PackManagedFile { Source = "files/agent.md", Target = "agent.md" }
        );

        await Assert.That(selector.Value?.Kind).IsEqualTo(PackManagedFileSelectorKind.File);
        await Assert.That(selector.Value?.Value).IsEqualTo("files/agent.md");
        await Assert.That(selector.Value?.SourceAlias).IsNull();
        await Assert.That(selector.Value?.IsExternal).IsFalse();
    }

    [Test]
    public async Task Selector_WhenPathDeclared_ResolvesDirectFile()
    {
        var selector = PackManagedFileSelector.Create(
            new PackManifest.PackManagedFile { Path = "files/agent.md", Target = "agent.md" }
        );

        await Assert.That(selector.Value?.Kind).IsEqualTo(PackManagedFileSelectorKind.File);
        await Assert.That(selector.Value?.Value).IsEqualTo("files/agent.md");
        await Assert.That(selector.Value?.IsExternal).IsFalse();
    }

    [Test]
    public async Task Selector_WhenLegacyAndCanonicalDirectFiles_NormalizeIdentically()
    {
        var legacySelector = PackManagedFileSelector.Create(
            new PackManifest.PackManagedFile { Source = "files/agent.md", Target = "agent.md" }
        );
        var canonicalSelector = PackManagedFileSelector.Create(
            new PackManifest.PackManagedFile { Path = "files/agent.md", Target = "agent.md" }
        );

        await Assert.That(legacySelector.Value?.Kind).IsEqualTo(canonicalSelector.Value?.Kind);
        await Assert.That(legacySelector.Value?.Value).IsEqualTo(canonicalSelector.Value?.Value);
        await Assert
            .That(legacySelector.Value?.SourceAlias)
            .IsEqualTo(canonicalSelector.Value?.SourceAlias);
        await Assert
            .That(legacySelector.Value?.Exclusions)
            .IsEquivalentTo(canonicalSelector.Value?.Exclusions ?? []);
        await Assert
            .That(legacySelector.Value?.Flatten)
            .IsEqualTo(canonicalSelector.Value?.Flatten);
    }

    [Test]
    public async Task Selector_WhenSourceAliasAndPathDeclared_ResolvesExternalFile()
    {
        var selector = PackManagedFileSelector.Create(
            new PackManifest.PackManagedFile
            {
                Source = "shared",
                Path = "docs/agent.md",
                Target = "agent.md",
            }
        );

        await Assert.That(selector.Value?.IsExternal).IsTrue();
        await Assert.That(selector.Value?.SourceAlias).IsEqualTo("shared");
        await Assert.That(selector.Value?.Value).IsEqualTo("docs/agent.md");
    }

    [Test]
    public async Task Selector_WhenGlobDeclaredWithExclusions_PreservesExclusions()
    {
        var selector = PackManagedFileSelector.Create(
            new PackManifest.PackManagedFile
            {
                Source = "shared",
                Glob = "docs/**/*.md",
                Exclude = ["docs/internal/**"],
                Flatten = true,
                Target = "docs",
            }
        );

        await Assert.That(selector.Value?.Kind).IsEqualTo(PackManagedFileSelectorKind.Glob);
        await Assert.That(selector.Value?.Flatten).IsTrue();
        await Assert.That(selector.Value?.Exclusions.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Selector_WhenMultiplePrimarySelectors_IsRejected()
    {
        var selector = PackManagedFileSelector.Create(
            new PackManifest.PackManagedFile
            {
                Path = "docs/agent.md",
                Glob = "docs/**/*.md",
                Target = "docs",
            }
        );

        await Assert.That(selector.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Selector_WhenFileSelectorUsesFlatten_IsRejected()
    {
        var selector = PackManagedFileSelector.Create(
            new PackManifest.PackManagedFile
            {
                Path = "docs/agent.md",
                Flatten = true,
                Target = "agent.md",
            }
        );

        await Assert.That(selector.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Selector_WhenNoSelectorDeclared_IsRejected()
    {
        var selector = PackManagedFileSelector.Create(
            new PackManifest.PackManagedFile { Target = "agent.md" }
        );

        await Assert.That(selector.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Validator_WhenSelectorAliasIsUnknown_IsRejected()
    {
        var manifest = CreatePackManifest();
        manifest.ManagedFiles =
        [
            new PackManifest.PackManagedFile
            {
                Source = "missing",
                Path = "docs/agent.md",
                Target = "agent.md",
            },
        ];

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert
            .That(issues.Any(issue => issue.Contains("missing", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task Validator_WhenPackSourceIsNotGit_IsRejected()
    {
        var manifest = CreatePackManifest();
        manifest.Sources["shared"] = new PackManifest.PackSource
        {
            Type = "local",
            Url = "https://example.test/owner/packs.git",
            Ref = "refs/heads/main",
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task Validator_WhenPackSourceOmitsRef_IsRejected()
    {
        var manifest = CreatePackManifest();
        manifest.Sources["shared"] = new PackManifest.PackSource
        {
            Type = "git",
            Url = "https://example.test/owner/packs.git",
            Ref = string.Empty,
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task PackSchema_WhenSourcesDeclared_RestrictsTypeToGit()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "pack.schema.json"))
        );
        var packSource = schema.RootElement.GetProperty("definitions").GetProperty("packSource");
        var required = packSource
            .GetProperty("required")
            .EnumerateArray()
            .Select(x => x.GetString());
        var typeValues = packSource
            .GetProperty("properties")
            .GetProperty("type")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(x => x.GetString());

        await Assert.That(required).Contains("url");
        await Assert.That(required).Contains("ref");
        await Assert.That(typeValues).Contains("git");
    }

    [Test]
    public async Task LockSchema_WhenManagedFileHasProvenance_RequiresCompleteRecord()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestData", "lunapack-lock.schema.json")
            )
        );
        var dependencies = schema
            .RootElement.GetProperty("definitions")
            .GetProperty("managedFile")
            .GetProperty("dependencies");

        await Assert
            .That(
                dependencies
                    .GetProperty("sourceAlias")
                    .EnumerateArray()
                    .Select(value => value.GetString())
            )
            .Contains("sourceFingerprint");
    }

    private static PackManifest CreatePackManifest() =>
        new()
        {
            Id = "example",
            Version = "1.0.0",
            Author = "Lunaris Digital Solutions",
            License = "MIT",
        };
}
