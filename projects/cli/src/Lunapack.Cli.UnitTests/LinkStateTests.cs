using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace Lunapack.Cli.UnitTests;

public sealed class LinkStateTests
{
    [Test]
    public async Task LoadAndSave_WhenLinkContainsWindowsPaths_UsesCanonicalPaths()
    {
        var fileSystem = new MockFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData(
                "schemaVersion: 1\nsources:\n  - name: upstream\n    type: local\n    path: 'packs\\catalog'\npacks: []\nlinks:\n  agents-csharp-expert:\n    source: upstream\n    path: 'agents\\dotnet'\n    includes:\n      - 'expert\\CSharpExpert.agent.md'\n    excludes:\n      - 'expert\\draft.agent.md'\n    stripPrefix: 'expert'\n    target: '.github\\agents'\ntrust:\n  sources: []\n  packs: []\n"
            )
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData(
                "schemaVersion: 1\npacks: []\nlinks:\n  agents-csharp-expert:\n    sourceName: upstream\n    sourceIdentity:\n      type: local\n      path: 'packs\\catalog'\n    definitionSha256: 0000000000000000000000000000000000000000000000000000000000000000\n    files:\n      - sourcePath: 'agents\\dotnet\\expert\\CSharpExpert.agent.md'\n        declaredTargetPath: '.github\\agents\\CSharpExpert.agent.md'\n        targetPath: '.github\\agents\\CSharpExpert.agent.md'\n        sha256: 0000000000000000000000000000000000000000000000000000000000000000\n"
            )
        );
        var stateStore = new ProjectStateStore(fileSystem);

        var loaded = await stateStore.LoadAsync(projectDirectory);
        var saved = await stateStore.SaveAsync(projectDirectory, loaded.RequireValue());

        await Assert.That(saved.IsSuccess).IsTrue();
        var link = loaded.RequireValue().Configuration.Links["agents-csharp-expert"];
        await Assert.That(link.Path).IsEqualTo("agents/dotnet");
        await Assert.That(link.Includes.Single()).IsEqualTo("expert/CSharpExpert.agent.md");
        await Assert.That(link.Excludes.Single()).IsEqualTo("expert/draft.agent.md");
        await Assert.That(link.StripPrefix).IsEqualTo("expert");
        await Assert.That(link.Target).IsEqualTo(".github/agents");
        var lockedFile = loaded
            .RequireValue()
            .LockFile.Links["agents-csharp-expert"]
            .Files.Single();
        await Assert
            .That(lockedFile.SourcePath)
            .IsEqualTo("agents/dotnet/expert/CSharpExpert.agent.md");
        await Assert.That(lockedFile.TargetPath).IsEqualTo(".github/agents/CSharpExpert.agent.md");
        var persistedLock = fileSystem.File.ReadAllText(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName)
        );
        await Assert.That(persistedLock).DoesNotContain("\\");
    }

    [Test]
    public async Task Save_WhenProjectHasNoLinks_OmitsEmptyLinkCollections()
    {
        var fileSystem = new MockFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var stateStore = new ProjectStateStore(fileSystem);

        var saved = await stateStore.SaveAsync(
            projectDirectory,
            new ProjectState
            {
                Configuration = new ProjectConfiguration { SchemaVersion = 1 },
                LockFile = new ProjectLockFile { SchemaVersion = 1 },
            }
        );
        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert.That(loaded.RequireValue().Configuration.Links).IsEmpty();
        await Assert.That(loaded.RequireValue().LockFile.Links).IsEmpty();
    }

    [Test]
    public async Task Validate_WhenLinkNameIsNotPackIdSyntax_IsRejected()
    {
        var configuration = CreateConfiguration("agents_expert", CreateLink());

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).Contains("Link name 'agents_expert' must use pack-ID syntax.");
    }

    [Test]
    public async Task Validate_WhenLinkSourceIsNotConfigured_IsRejected()
    {
        var link = CreateLink();
        link.Source = "missing";
        var configuration = CreateConfiguration("agents-expert", link);

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert
            .That(issues)
            .Contains("Link 'agents-expert' must reference a configured source name.");
    }

    [Test]
    public async Task Validate_WhenLinkHasNoIncludes_IsRejected()
    {
        var link = CreateLink();
        link.Includes.Clear();
        var configuration = CreateConfiguration("agents-expert", link);

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert
            .That(issues)
            .Contains("Link 'agents-expert' must declare at least one non-empty include pattern.");
    }

    [Test]
    [Arguments("../escape.md")]
    [Arguments("/rooted.md")]
    public async Task Validate_WhenIncludeEscapesSourceRoot_IsRejected(string include)
    {
        var link = CreateLink();
        link.Includes = [include];
        var configuration = CreateConfiguration("agents-expert", link);

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert
            .That(issues)
            .Contains("Link 'agents-expert' patterns must be safe source-relative paths.");
    }

    [Test]
    public async Task Validate_WhenResolvedLinkFileHashIsInvalid_IsRejected()
    {
        var lockFile = new ProjectLockFile
        {
            SchemaVersion = 1,
            Links =
            {
                ["agents-expert"] = new ProjectLockFile.ResolvedLink
                {
                    SourceName = "upstream",
                    SourceIdentity = ConfiguredSourceIdentity.CreateLocal("packs/catalog"),
                    DefinitionSha256 = new string('a', 64),
                    Files =
                    [
                        new ProjectLockFile.LinkFile
                        {
                            SourcePath = "agents/CSharpExpert.agent.md",
                            DeclaredTargetPath = ".github/agents/CSharpExpert.agent.md",
                            TargetPath = ".github/agents/CSharpExpert.agent.md",
                            Sha256 = "not-a-hash",
                        },
                    ],
                },
            },
        };

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert
            .That(issues)
            .Contains(
                "Resolved link 'agents-expert' files must define safe source, declared, and effective target paths and a SHA-256 hash."
            );
    }

    [Test]
    public async Task Load_WhenLockContainsUnknownLink_IsRejected()
    {
        var fileSystem = new MockFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName),
            new MockFileData("schemaVersion: 1\nsources: []\npacks: []\n")
        );
        fileSystem.AddFile(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName),
            new MockFileData(
                "schemaVersion: 1\npacks: []\nlinks:\n  agents-expert:\n    sourceName: upstream\n    sourceIdentity:\n      type: local\n      path: packs/catalog\n    definitionSha256: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n    files: []\n"
            )
        );
        var stateStore = new ProjectStateStore(fileSystem);

        var loaded = await stateStore.LoadAsync(projectDirectory);

        await Assert.That(loaded.IsSuccess).IsFalse();
        await Assert
            .That(loaded.Error)
            .Contains(
                "Lock file contains link 'agents-expert' that is not defined in the project configuration."
            );
    }

    [Test]
    public async Task Save_WhenLinkNameCollidesWithRequestedPack_IsRejected()
    {
        var fileSystem = new MockFileSystem();
        const string projectDirectory = @"C:\project";
        fileSystem.AddDirectory(projectDirectory);
        var link = CreateLink();
        var configuration = CreateConfiguration("example", link);
        configuration.Packs.Add(new ProjectConfiguration.RequestedPack { Id = "example" });
        var stateStore = new ProjectStateStore(fileSystem);

        var saved = await stateStore.SaveAsync(
            projectDirectory,
            new ProjectState
            {
                Configuration = configuration,
                LockFile = new ProjectLockFile { SchemaVersion = 1 },
            }
        );

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert
            .That(saved.Error)
            .Contains(
                "Project configuration uses 'example' as both a link name and a requested pack ID."
            );
    }

    [Test]
    public async Task ComputeSha256_WhenSelectorOrderDiffers_ProducesStableDigest()
    {
        var first = CreateLink();
        first.Includes = ["b.md", "a.md", "a.md"];
        var second = CreateLink();
        second.Includes = ["a.md", "b.md"];

        await Assert
            .That(LinkDefinitionHasher.ComputeSha256("agents-expert", first))
            .IsEqualTo(LinkDefinitionHasher.ComputeSha256("agents-expert", second));
    }

    [Test]
    public async Task ComputeSha256_WhenOptionalPathsAreOmitted_MatchesEmptyStringForm()
    {
        var omitted = CreateLink();
        var empty = CreateLink();
        empty.Target = string.Empty;
        empty.StripPrefix = string.Empty;
        empty.Path = string.Empty;

        await Assert
            .That(LinkDefinitionHasher.ComputeSha256("agents-expert", omitted))
            .IsEqualTo(LinkDefinitionHasher.ComputeSha256("agents-expert", empty));
    }

    [Test]
    public async Task ComputeSha256_WhenSemanticFieldChanges_ProducesDifferentDigest()
    {
        var baseline = CreateLink();
        var flattened = CreateLink();
        flattened.Flatten = true;

        await Assert
            .That(LinkDefinitionHasher.ComputeSha256("agents-expert", baseline))
            .IsNotEqualTo(LinkDefinitionHasher.ComputeSha256("agents-expert", flattened));
    }

    [Test]
    public async Task ProjectSchema_WhenLinksDeclared_ConstrainsNamesAndProperties()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestData", "lunapack.schema.json")
            )
        );
        var links = schema.RootElement.GetProperty("properties").GetProperty("links");
        var link = schema.RootElement.GetProperty("definitions").GetProperty("link");

        await Assert
            .That(links.GetProperty("propertyNames").GetProperty("$ref").GetString())
            .IsEqualTo("#/definitions/linkName");
        await Assert.That(link.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert
            .That(link.GetProperty("required").EnumerateArray().Select(item => item.GetString()))
            .Contains("includes");
        await Assert
            .That(link.GetProperty("properties").TryGetProperty("parameters", out _))
            .IsFalse();
        await Assert
            .That(link.GetProperty("properties").TryGetProperty("scripts", out _))
            .IsFalse();
    }

    [Test]
    public async Task LockSchema_WhenLinksDeclared_RequiresSourceEvidenceAndDefinitionHash()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestData", "lunapack-lock.schema.json")
            )
        );
        var resolvedLink = schema
            .RootElement.GetProperty("definitions")
            .GetProperty("resolvedLink");

        await Assert
            .That(
                resolvedLink
                    .GetProperty("required")
                    .EnumerateArray()
                    .Select(item => item.GetString())
            )
            .Contains("definitionSha256");
        await Assert
            .That(
                resolvedLink
                    .GetProperty("required")
                    .EnumerateArray()
                    .Select(item => item.GetString())
            )
            .Contains("sourceIdentity");
        await Assert.That(resolvedLink.GetProperty("additionalProperties").GetBoolean()).IsFalse();
    }

    private static ProjectConfiguration CreateConfiguration(
        string name,
        ProjectConfiguration.Link link
    ) =>
        new()
        {
            SchemaVersion = 1,
            Sources =
            [
                new ProjectConfiguration.LocalSource { Name = "upstream", Path = "packs/catalog" },
            ],
            Links = { [name] = link },
        };

    private static ProjectConfiguration.Link CreateLink() =>
        new() { Source = "upstream", Includes = ["CSharpExpert.agent.md"] };
}
