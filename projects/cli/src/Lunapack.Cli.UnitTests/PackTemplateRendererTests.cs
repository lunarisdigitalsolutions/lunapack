using System.Globalization;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.ManagedFiles;

namespace Lunapack.Cli.UnitTests;

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
        var fileSystem = CreateFileSystem("{{ date.now.year }}");
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
    public async Task RenderManagedFile_WhenCurrentAndTargetRemapped_RendersPortableRelativePath()
    {
        var fileSystem = CreateFileSystem(
            "{{ files.relative_path 'docs/development/code-review.md' }}"
        );
        var result = new PackTemplateRenderer(fileSystem).RenderManagedFile(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions"),
            CreateManagedFileContext(".github/agents/review/core.agent.md")
        );

        await Assert
            .That(Encoding.UTF8.GetString(result.RequireValue().Contents))
            .IsEqualTo("../../../docs/04-development/process/code-review.md");
    }

    [Test]
    public async Task RenderManagedFile_WhenCurrentTargetAtRoot_RendersRelativeTarget()
    {
        var fileSystem = CreateFileSystem(
            "{{ files.relative_path 'docs/development/code-review.md' }}"
        );
        var result = new PackTemplateRenderer(fileSystem).RenderManagedFile(
            TemplatePath,
            true,
            CreateParameters("Lunaris Digital Solutions"),
            CreateManagedFileContext("README.md")
        );

        await Assert
            .That(Encoding.UTF8.GetString(result.RequireValue().Contents))
            .IsEqualTo("docs/04-development/process/code-review.md");
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
