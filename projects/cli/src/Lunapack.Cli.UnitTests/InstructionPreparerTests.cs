using System.Globalization;
using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Instructions;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.UnitTests;

public sealed class InstructionPreparerTests
{
    [Test]
    public async Task Prepare_WhenTemplatingDisabled_PreservesStaticContent()
    {
        var (fileSystem, pack) = CreatePack("## Setup\n{{ unknown }}");

        var result = Prepare(fileSystem, pack, templating: false);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Document.Steps[0].Content)
            .IsEqualTo("{{ unknown }}");
    }

    [Test]
    public async Task Prepare_WhenConditionUsesResolvedParameter_SelectsMatchingContent()
    {
        var (fileSystem, pack) = CreatePack(
            "## Setup\n{{ if enabled }}Enabled{{ else }}Disabled{{ end }}"
        );

        var result = Prepare(fileSystem, pack, templating: true);

        await Assert.That(result.RequireValue().Document.Steps[0].Content).IsEqualTo("Enabled");
    }

    [Test]
    public async Task Prepare_WhenMultiSelectContainsValue_SelectsMatchingContent()
    {
        var (fileSystem, pack) = CreatePack(
            "## Setup\n{{ if features contains \"docker\" }}Docker{{ else }}Other{{ end }}"
        );

        var result = Prepare(fileSystem, pack, templating: true);

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert.That(result.RequireValue().Document.Steps[0].Content).IsEqualTo("Docker");
    }

    [Test]
    public async Task Prepare_WhenTemplateUsesCurrentTime_RendersCurrentYear()
    {
        var (fileSystem, pack) = CreatePack("## Setup\n{{ date.now.year }}");

        var result = Prepare(fileSystem, pack, templating: true);

        await Assert
            .That(result.RequireValue().Document.Steps[0].Content)
            .IsEqualTo(DateTime.Now.Year.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task Prepare_WhenFileUsesWindowsSeparators_NormalizesRelativePath()
    {
        var (fileSystem, pack) = CreatePack("Setup");

        var result = Prepare(fileSystem, pack, templating: false, file: @"instructions\setup.md");

        await Assert
            .That(result.RequireValue().PackedFile.RelativePath)
            .IsEqualTo("instructions/setup.md");
    }

    [Test]
    public async Task Prepare_WhenFileMissing_ReturnsFailure()
    {
        var (fileSystem, pack) = CreatePack("Setup");

        var result = Prepare(fileSystem, pack, templating: false, file: "instructions/missing.md");

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Prepare_WhenFileTraversesOutsidePack_ReturnsFailure()
    {
        var (fileSystem, pack) = CreatePack("Setup");

        var result = Prepare(fileSystem, pack, templating: false, file: "../outside.md");

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Prepare_WhenContentIsInvalidUtf8_ReturnsFailure()
    {
        var (fileSystem, pack) = CreatePack([0xFF, 0xFE]);

        var result = Prepare(fileSystem, pack, templating: false);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Prepare_WhenTemplateInvalid_ReturnsFailure()
    {
        var (fileSystem, pack) = CreatePack("{{ 1 + }}");

        var result = Prepare(fileSystem, pack, templating: true);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Prepare_WhenTemplateUsesUnknownVariable_ReturnsFailure()
    {
        var (fileSystem, pack) = CreatePack("{{ unknown }}");

        var result = Prepare(fileSystem, pack, templating: true);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    private static ManifestOperationResult<PreparedInstruction> Prepare(
        MockFileSystem fileSystem,
        DiscoveredPack pack,
        bool templating,
        string file = "instructions/setup.md"
    ) =>
        new InstructionPreparer(fileSystem).Prepare(
            pack,
            new PackManifest.PackHook
            {
                Type = "instruction",
                File = file,
                Templating = templating,
            },
            CreateParameters()
        );

    private static (MockFileSystem FileSystem, DiscoveredPack Pack) CreatePack(string content) =>
        CreatePack(new MockFileData(content));

    private static (MockFileSystem FileSystem, DiscoveredPack Pack) CreatePack(byte[] content) =>
        CreatePack(new MockFileData(content));

    private static (MockFileSystem FileSystem, DiscoveredPack Pack) CreatePack(MockFileData content)
    {
        var fileSystem = new MockFileSystem();
        var snapshotPath = fileSystem.Path.GetFullPath("snapshot");
        var packPath = fileSystem.Path.Combine(snapshotPath, "example");
        fileSystem.AddDirectory(packPath);
        fileSystem.AddFile(fileSystem.Path.Combine(packPath, "instructions", "setup.md"), content);
        var pack = new DiscoveredPack(
            snapshotPath,
            packPath,
            new PackManifest { Id = "example", Version = "1.0.0" },
            "local",
            ConfiguredSourceIdentity.CreateLocal("source")
        );
        return (fileSystem, pack);
    }

    private static ResolvedPackParameters CreateParameters() =>
        new(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal)
            {
                ["enabled"] = new(PackParameterType.Bool, true, []),
                ["features"] = new(
                    PackParameterType.Enum,
                    false,
                    ["api", "docker"],
                    Multiple: true
                ),
            },
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
            {
                ["enabled"] = new(PackParameterType.Bool, string.Empty, true),
                ["features"] = new(PackParameterType.Enum, string.Empty, false, ["api", "docker"]),
            }
        );
}
