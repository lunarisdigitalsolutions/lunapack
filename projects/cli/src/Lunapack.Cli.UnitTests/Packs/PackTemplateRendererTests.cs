using System.Globalization;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.ManagedFiles;

namespace Lunapack.Cli.UnitTests.Packs;

public sealed class PackTemplateRendererTests
{
    [Test]
    public async Task Render_WhenResolvedParametersProvided_RendersUtf8Content()
    {
        var fileSystem = CreateFileSystem("Copyright {{ companyName }}");
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions")
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert
            .That(Encoding.UTF8.GetString(result.RequireValue()))
            .IsEqualTo("Copyright Lunaris Digital Solutions");
    }

    [Test]
    public async Task Render_WhenMultiSelectContainsValue_UsesScribanMembership()
    {
        var fileSystem = CreateFileSystem(
            "{{ if features contains \"docker\" }}Docker is enabled.{{ end }}"
        );
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            true,
            CreateMultiSelectParameters(["api", "docker"])
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert
            .That(Encoding.UTF8.GetString(result.RequireValue()))
            .IsEqualTo("Docker is enabled.");
    }

    [Test]
    public async Task Render_WhenMultiSelectDoesNotContainValue_OmitsBranch()
    {
        var fileSystem = CreateFileSystem(
            "{{ if features contains \"docker\" }}Docker is enabled.{{ end }}"
        );
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            true,
            CreateMultiSelectParameters([])
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(result.RequireValue())).IsEmpty();
    }

    [Test]
    public async Task Render_WhenTemplateUsesCurrentDate_RendersCurrentYear()
    {
        var fileSystem = CreateFileSystem("{{ date.now | date.to_string \"%Y\" }}");
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions")
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(Encoding.UTF8.GetString(result.RequireValue()))
            .IsEqualTo(DateTime.Now.Year.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task Render_WhenTemplateReferencesUnknownVariable_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem("{{ unknownVariable }}");
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions")
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Render_WhenTemplateDisabled_PreservesLiteralContent()
    {
        var fileSystem = CreateFileSystem("{{ unknownVariable }}");
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            false,
            CreateParameters("Lunaris Digital Solutions")
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(Encoding.UTF8.GetString(result.RequireValue()))
            .IsEqualTo("{{ unknownVariable }}");
    }

    [Test]
    public async Task Render_WhenInputMatchesEffectiveLimit_AcceptsBoundary()
    {
        const string content = "static content";
        var fileSystem = CreateFileSystem(content);
        var resourceLimits = ResourceLimits.Default with
        {
            PackDocumentBytes = Encoding.UTF8.GetByteCount(content),
        };
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            false,
            CreateParameters("Lunaris Digital Solutions"),
            resourceLimits
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert.That(Encoding.UTF8.GetString(result.RequireValue())).IsEqualTo(content);
    }

    [Test]
    public async Task Render_WhenInputExceedsEffectiveLimit_RejectsWithLowerBound()
    {
        const string content = "static content";
        var fileSystem = CreateFileSystem(content);
        var resourceLimits = ResourceLimits.Default with
        {
            PackDocumentBytes = Encoding.UTF8.GetByteCount(content) - 1,
        };
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            false,
            CreateParameters("Lunaris Digital Solutions"),
            resourceLimits
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("Pack document");
        await Assert.That(result.Error).Contains("byte limit");
        await Assert.That(result.Error).Contains("at least");
    }

    [Test]
    public async Task Render_WhenUtf8OutputMatchesPerFileLimit_AcceptsBoundary()
    {
        var fileSystem = CreateFileSystem("{{ companyName }}");
        var resourceLimits = ResourceLimits.Default with { RenderedFileBytes = 3 };
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            true,
            CreateParameters("\u20ac"),
            resourceLimits
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert.That(result.RequireValue().Length).IsEqualTo(3);
    }

    [Test]
    public async Task Render_WhenUtf8OutputExceedsPerFileLimit_RejectsWhileWriting()
    {
        var fileSystem = CreateFileSystem("{{ companyName }}");
        var resourceLimits = ResourceLimits.Default with { RenderedFileBytes = 2 };
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            true,
            CreateParameters("\u20ac"),
            resourceLimits
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("2 byte file limit");
        await Assert.That(result.Error).Contains("at least 3 bytes produced");
    }

    [Test]
    public async Task Render_WhenSharedTotalLimitExceeds_RejectsSecondOutput()
    {
        var fileSystem = CreateFileSystem("1234");
        fileSystem.AddFile("C:\\pack\\second.txt", new MockFileData("5678"));
        var resourceLimits = ResourceLimits.Default with { RenderedTotalBytes = 7 };
        var budget = new RenderedOutputBudget(resourceLimits);
        var renderer = new PackTemplateRenderer(fileSystem);

        var first = renderer.Render(
            TemplatePath,
            false,
            CreateParameters("unused"),
            resourceLimits,
            budget
        );
        var second = renderer.Render(
            "C:\\pack\\second.txt",
            false,
            CreateParameters("unused"),
            resourceLimits,
            budget
        );

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(second.IsSuccess).IsFalse();
        await Assert.That(second.Error).Contains("7 byte total limit");
        await Assert.That(second.Error).Contains("at least 8 bytes produced");
    }

    [Test]
    public async Task Render_WhenTemplateInvalid_ReturnsFailure()
    {
        var fileSystem = CreateFileSystem("{{ 1 + }}");
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions")
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Render_WhenTemplateIsNotUtf8_ReturnsFailure()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory("C:\\pack");
        fileSystem.AddFile(TemplatePath, new MockFileData([0xFF, 0xFE]));
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions")
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task RenderManagedFile_WhenTargetRemapped_RendersEffectivePath()
    {
        var fileSystem = CreateFileSystem("{{ files.path 'docs/development/code-review.md' }}");
        var result = new PackTemplateRenderer(fileSystem).RenderManagedFile(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions"),
            CreateManagedFileContext("docs/index.md")
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(Encoding.UTF8.GetString(result.RequireValue().Contents))
            .IsEqualTo("docs/04-development/process/code-review.md");
        await Assert.That(result.RequireValue().Diagnostics).IsEmpty();
    }

    [Test]
    [Arguments(
        ".github/agents/review/core.agent.md",
        "../../../docs/04-development/process/code-review.md"
    )]
    [Arguments("README.md", "docs/04-development/process/code-review.md")]
    public async Task RenderManagedFile_WhenResolvingRelativePath_RendersExpectedTarget(
        string currentTarget,
        string expectedPath
    )
    {
        var fileSystem = CreateFileSystem(
            "{{ files.relative_path 'docs/development/code-review.md' }}"
        );
        var result = new PackTemplateRenderer(fileSystem).RenderManagedFile(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions"),
            CreateManagedFileContext(currentTarget)
        );

        await Assert
            .That(Encoding.UTF8.GetString(result.RequireValue().Contents))
            .IsEqualTo(expectedPath);
    }

    [Test]
    public async Task RenderManagedFile_WhenTargetMissing_PreservesTargetAndRecordsDiagnostic()
    {
        var fileSystem = CreateFileSystem("{{ files.path 'docs/missing.md' }}");
        var result = new PackTemplateRenderer(fileSystem).RenderManagedFile(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions"),
            CreateManagedFileContext("docs/index.md")
        );

        await Assert
            .That(Encoding.UTF8.GetString(result.RequireValue().Contents))
            .IsEqualTo("docs/missing.md");
        await Assert
            .That(result.RequireValue().Diagnostics)
            .IsEquivalentTo([
                new ManagedFileTemplateDiagnostic("docs/missing.md", "docs/index.md"),
            ]);
    }

    [Test]
    public async Task Render_WhenManagedFileContextAbsent_RejectsFilesObject()
    {
        var fileSystem = CreateFileSystem("{{ files.path 'docs/index.md' }}");
        var result = new PackTemplateRenderer(fileSystem).Render(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions")
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    private const string TemplatePath = "C:\\pack\\template.txt";

    private static MockFileSystem CreateFileSystem(string content)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory("C:\\pack");
        fileSystem.AddFile(TemplatePath, new MockFileData(content));
        return fileSystem;
    }

    private static ResolvedPackParameters CreateParameters(string companyName) =>
        new(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal)
            {
                ["companyName"] = new(PackParameterType.String, true, []),
            },
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
            {
                ["companyName"] = new(PackParameterType.String, companyName, false),
            }
        );

    private static ResolvedPackParameters CreateMultiSelectParameters(
        IReadOnlyList<string> features
    ) =>
        new(
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
                ["features"] = new(PackParameterType.Enum, string.Empty, false, features),
            }
        );

    private static ManagedFileTemplateContext CreateManagedFileContext(
        string currentEffectiveTarget
    ) =>
        new(
            currentEffectiveTarget,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["docs/development/code-review.md"] = "docs/04-development/process/code-review.md",
            }
        );
}
