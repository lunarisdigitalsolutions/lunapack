using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests.Catalog;

public sealed class PackCatalogTests
{
    private static readonly string _projectDirectory = Path.GetFullPath("project");
    private static readonly string _packsDirectory = Path.GetFullPath("packs");
    private static readonly string _firstDirectory = Path.GetFullPath("first");
    private static readonly string _secondDirectory = Path.GetFullPath("second");

    [Test]
    public async Task Browse_WhenPacksAreNested_UsesManifestDirectoriesAsPackRoots()
    {
        var fileSystem = CreateFileSystem([
            (PacksPath("releases", "first", "pack.yml"), CreatePack("first", "1.0.0")),
            (PacksPath("releases", "second", "pack.yml"), CreatePack("second", "1.0.0")),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.BrowseAsync(_projectDirectory, CreateManifest(_packsDirectory));

        await Assert.That(result.IsSuccess).IsTrue();
        var packs = result.RequireValue();
        await Assert.That(packs).Count().IsEqualTo(2);
        await Assert
            .That(
                packs
                    .Single(pack =>
                        string.Equals(pack.Manifest.Id, "first", StringComparison.Ordinal)
                    )
                    .PackDirectory
            )
            .IsEqualTo(PacksPath("releases", "first"));
    }

    [Test]
    public async Task Scenario_BundledManifests_DiscoverAllDescribedPacks()
    {
        var fileSystem = CreateBundledManifestFileSystem();
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.BrowseAsync(_projectDirectory, CreateManifest(_packsDirectory));

        await Assert.That(result.IsSuccess).IsTrue();
        var packs = result.RequireValue();
        await Assert.That(packs).Count().IsEqualTo(9);
        await Assert.That(packs.All(pack => pack.Manifest.Description is not null)).IsTrue();
        await Assert
            .That(
                packs
                    .Select(pack => pack.Manifest.Id)
                    .OrderBy(packId => packId, StringComparer.Ordinal)
            )
            .IsEquivalentTo([
                "csharpier",
                "dotnet-build-config",
                "dotnet-central-package-management",
                "dotnet-editorconfig",
                "dotnet-gitignore",
                "dotnet-project",
                "dotnet-sdk-10",
                "license-mit",
                "madr-template",
            ]);
    }

    private static MockFileSystem CreateBundledManifestFileSystem() =>
        CreateFileSystem([
            (
                PacksPath("dotnet-gitignore", "pack.yml"),
                CreatePack("dotnet-gitignore", "1.0.0", "Ignore .NET artifacts")
            ),
            (
                PacksPath("dotnet-sdk-10", "pack.yml"),
                CreatePack("dotnet-sdk-10", "1.0.0", "Pin the .NET SDK")
            ),
            (
                PacksPath("dotnet-editorconfig", "pack.yml"),
                CreatePack("dotnet-editorconfig", "1.0.0", "Apply formatting conventions")
            ),
            (PacksPath("csharpier", "pack.yml"), CreatePack("csharpier", "1.0.0", "Pin CSharpier")),
            (
                PacksPath("dotnet-build-config", "pack.yml"),
                CreatePack("dotnet-build-config", "1.0.0", "Apply build policy")
            ),
            (
                PacksPath("dotnet-central-package-management", "pack.yml"),
                CreatePack(
                    "dotnet-central-package-management",
                    "1.0.0",
                    "Configure central package management"
                )
            ),
            (
                PacksPath("dotnet-project", "pack.yml"),
                CreatePack("dotnet-project", "1.0.0", "Apply .NET project policy")
            ),
            (
                PacksPath("madr-template", "pack.yml"),
                CreatePack("madr-template", "1.0.0", "Start decisions with MADR")
            ),
            (
                PacksPath("license-mit", "pack.yml"),
                "id: license-mit\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\ndescription: Manage a parameterized MIT license\nparameters:\n  companyName:\n    type: string\n    required: true\nmanagedFiles:\n  - source: templates/LICENSE.md\n    target: LICENSE.md\n"
            ),
        ]);

    [Test]
    public async Task Browse_WhenCandidateManifestInvalid_ExcludesCandidate()
    {
        var fileSystem = CreateFileSystem([
            (PacksPath("invalid", "pack.yml"), "id: invalid\nversion: invalid\n"),
            (PacksPath("valid", "pack.yml"), CreatePack("valid", "1.0.0")),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.BrowseAsync(_projectDirectory, CreateManifest(_packsDirectory));

        await Assert.That(result.IsSuccess).IsTrue();
        var packs = result.RequireValue();
        await Assert.That(packs).Count().IsEqualTo(1);
        await Assert.That(packs[0].Manifest.Id).IsEqualTo("valid");
    }

    [Test]
    public async Task Browse_WhenCandidateMissingRequiredMetadata_ExcludesCandidate()
    {
        var fileSystem = CreateFileSystem([
            (
                PacksPath("missing-author", "pack.yml"),
                "id: missing-author\nversion: 1.0.0\nlicense: MIT\n"
            ),
            (
                PacksPath("missing-license", "pack.yml"),
                "id: missing-license\nversion: 1.0.0\nauthor: Example Author\n"
            ),
            (PacksPath("valid", "pack.yml"), CreatePack("valid", "1.0.0")),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.BrowseAsync(_projectDirectory, CreateManifest(_packsDirectory));

        await Assert
            .That(result.RequireValue().Select(pack => pack.Manifest.Id))
            .IsEquivalentTo(["valid"]);
    }

    [Test]
    public async Task Browse_WhenCandidateSourceDirectoryMissing_ExcludesCandidate()
    {
        var fileSystem = CreateFileSystem([
            (
                PacksPath("invalid", "pack.yml"),
                "id: invalid\nversion: 1.0.0\nmanagedFiles:\n  - directory: files\n    target: files\n"
            ),
            (PacksPath("valid", "pack.yml"), CreatePack("valid", "1.0.0")),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.BrowseAsync(_projectDirectory, CreateManifest(_packsDirectory));

        await Assert.That(result.IsSuccess).IsTrue();
        var packs = result.RequireValue();
        await Assert.That(packs).Count().IsEqualTo(1);
        await Assert.That(packs.Single().Manifest.Id).IsEqualTo("valid");
    }

    [Test]
    public async Task Browse_WhenStrategyOmitted_UsesCopyOverwriteDefault()
    {
        var fileSystem = CreateFileSystem([
            (PacksPath("example", "pack.yml"), CreatePack("example", "1.0.0")),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.BrowseAsync(_projectDirectory, CreateManifest(_packsDirectory));

        var strategy = result.RequireValue().Single().Manifest.ManagedFiles.Single().Strategy;
        await Assert.That(strategy.Type).IsEqualTo("copy");
        await Assert.That(strategy.Method).IsEqualTo("overwrite");
    }

    [Test]
    public async Task Browse_WhenBooleanParameterHasDefault_PreservesTypedValue()
    {
        var fileSystem = CreateFileSystem([
            (
                PacksPath("example", "pack.yml"),
                "id: example\nversion: 1.0.0\nlicense: MIT\nauthor: Example Author\nparameters:\n  enabled:\n    type: bool\n    default: true\nmanagedFiles:\n  - source: templates/content.txt\n    target: example.txt\n"
            ),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.BrowseAsync(_projectDirectory, CreateManifest(_packsDirectory));

        var defaultValue = result.RequireValue().Single().Manifest.Parameters["enabled"].Default;
        await Assert.That(defaultValue).IsTypeOf<bool>();
        await Assert.That((bool)defaultValue!).IsTrue();
    }

    [Test]
    public async Task Browse_WhenSourceEmpty_ReturnsEmptyCatalog()
    {
        var fileSystem = CreateFileSystem([]);
        fileSystem.Directory.CreateDirectory(_packsDirectory);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.BrowseAsync(_projectDirectory, CreateManifest(_packsDirectory));

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue()).IsEmpty();
    }

    [Test]
    public async Task Resolve_WhenVersionExplicit_SelectsRequestedVersion()
    {
        var fileSystem = CreateFileSystem([
            (PacksPath("one", "pack.yml"), CreatePack("example", "1.0.0")),
            (PacksPath("two", "pack.yml"), CreatePack("example", "2.0.0")),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.ResolveAsync(
            _projectDirectory,
            CreateManifest(_packsDirectory),
            "example",
            "1.0.0"
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Manifest.Version).IsEqualTo("1.0.0");
    }

    [Test]
    public async Task Resolve_WhenOnlyPrereleasesAvailable_SelectsHighestPrecedence()
    {
        var fileSystem = CreateFileSystem([
            (PacksPath("alpha", "pack.yml"), CreatePack("example", "1.0.0-alpha")),
            (PacksPath("beta", "pack.yml"), CreatePack("example", "1.0.0-beta")),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.ResolveAsync(
            _projectDirectory,
            CreateManifest(_packsDirectory),
            "example",
            null
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Manifest.Version).IsEqualTo("1.0.0-beta");
    }

    [Test]
    public async Task Resolve_WhenVersionsEqual_SelectsEarliestConfiguredSource()
    {
        var fileSystem = CreateFileSystem([
            (FirstPath("release", "pack.yml"), CreatePack("example", "1.0.0")),
            (SecondPath("release", "pack.yml"), CreatePack("example", "1.0.0")),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());

        var result = await catalog.ResolveAsync(
            _projectDirectory,
            CreateManifest(_firstDirectory, _secondDirectory),
            "example",
            null
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().SourcePath).IsEqualTo(_firstDirectory);
        await Assert
            .That(result.RequireValue().SourceSelection)
            .IsEqualTo(new PackSourceSelection("example", "source-0", "local"));
    }

    [Test]
    public async Task Format_WhenDescriptionExceedsLimit_TruncatesPreviewToEightyCharacters()
    {
        var description = new string('a', 81);
        var fileSystem = CreateFileSystem([
            (PacksPath("example", "pack.yml"), CreatePack("example", "1.0.0", description)),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());
        var result = await catalog.BrowseAsync(_projectDirectory, CreateManifest(_packsDirectory));

        var output = CatalogOutputFormatter.Format(result.RequireValue()[0]);

        await Assert.That(output).IsEqualTo($"example@1.0.0 - {new string('a', 77)}...");
    }

    [Test]
    public async Task Search_WhenRelevanceDiffers_OrdersMatchesBySpecifiedTier()
    {
        var fileSystem = CreateFileSystem([
            (PacksPath("exact", "pack.yml"), CreatePack("cli", "1.0.0")),
            (PacksPath("prefix", "pack.yml"), CreatePack("cli-pack", "1.0.0")),
            (PacksPath("substring", "pack.yml"), CreatePack("pack-cli", "1.0.0")),
            (
                PacksPath("description", "pack.yml"),
                CreatePack("documentation", "1.0.0", "CLI reference")
            ),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());
        var browsedCatalog = await catalog.BrowseAsync(
            _projectDirectory,
            CreateManifest(_packsDirectory)
        );

        var matches = PackCatalog.Search(browsedCatalog.RequireValue(), "cli");

        await Assert.That(matches[0].Manifest.Id).IsEqualTo("cli");
        await Assert.That(matches[1].Manifest.Id).IsEqualTo("cli-pack");
        await Assert.That(matches[2].Manifest.Id).IsEqualTo("pack-cli");
        await Assert.That(matches[3].Manifest.Id).IsEqualTo("documentation");
    }

    [Test]
    public async Task Search_WhenTagMatches_ReturnsTaggedPack()
    {
        var fileSystem = CreateFileSystem([
            (
                PacksPath("tagged", "pack.yml"),
                CreatePack("engineering", "1.0.0", tags: ["compliance"])
            ),
        ]);
        var catalog = new PackCatalog(fileSystem, TestConsole.Create());
        var browsedCatalog = await catalog.BrowseAsync(
            _projectDirectory,
            CreateManifest(_packsDirectory)
        );

        var matches = PackCatalog.Search(browsedCatalog.RequireValue(), "compliance");

        await Assert.That(matches.Select(pack => pack.Manifest.Id)).IsEquivalentTo(["engineering"]);
    }

    private static MockFileSystem CreateFileSystem(
        IReadOnlyList<(string Path, string Contents)> files
    )
    {
        var fileSystem = new MockFileSystem();
        foreach (var file in files)
        {
            fileSystem.Directory.CreateDirectory(
                fileSystem.Path.GetDirectoryName(file.Path).RequireNotNull()
            );
            fileSystem.File.WriteAllText(file.Path, file.Contents);
            var sourceLine = file
                .Contents.Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("- source: ", StringComparison.Ordinal));
            var source = sourceLine?["- source: ".Length..];
            if (source is not null)
            {
                var sourcePath = fileSystem.Path.Combine(
                    fileSystem.Path.GetDirectoryName(file.Path).RequireNotNull(),
                    source
                );
                fileSystem.Directory.CreateDirectory(
                    fileSystem.Path.GetDirectoryName(sourcePath).RequireNotNull()
                );
                fileSystem.File.WriteAllText(sourcePath, "source");
            }
        }

        return fileSystem;
    }

    private static string PacksPath(params string[] paths) =>
        Path.Combine([_packsDirectory, .. paths]);

    private static string FirstPath(params string[] paths) =>
        Path.Combine([_firstDirectory, .. paths]);

    private static string SecondPath(params string[] paths) =>
        Path.Combine([_secondDirectory, .. paths]);

    private static ProjectManifest CreateManifest(params string[] sourcePaths) =>
        new()
        {
            Sources =
            [
                .. sourcePaths.Select(path => new ProjectManifest.LocalSource { Path = path }),
            ],
        };

    private static string CreatePack(
        string id,
        string version,
        string? description = null,
        IReadOnlyList<string>? tags = null
    )
    {
        var descriptionLine = description is null ? null : $"description: {description}\n";
        var tagsSection = tags is null
            ? null
            : $"tags:\n{string.Concat(tags.Select(tag => $"  - {tag}\n"))}";

        return $"id: {id}\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\n{descriptionLine}{tagsSection}managedFiles:\n  - source: source.txt\n    target: target.txt\n";
    }
}
