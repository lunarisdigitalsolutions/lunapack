using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Application.Paths;

namespace Lunapack.Cli.UnitTests.Application.Paths;

public sealed class ProjectPathTests
{
    [Test]
    public async Task Normalize_WhenWindowsSeparatorsProvided_ReturnsPortablePath()
    {
        await Assert
            .That(ProjectPath.Normalize(@"docs\guides\setup.md"))
            .IsEqualTo("docs/guides/setup.md");
    }

    [Test]
    [Arguments(@"docs\.\guides\..\README.md", "docs/README.md")]
    [Arguments("docs/guides/", "docs/guides")]
    [Arguments("Docs/README.md", "Docs/README.md")]
    public async Task NormalizeProjectRelativePath_WhenPathIsContained_ReturnsCanonicalPath(
        string path,
        string expected
    )
    {
        var fileSystem = new MockFileSystem();

        var result = ProjectPath.NormalizeProjectRelativePath(fileSystem, @"C:\workspace", path);

        await Assert.That(result.RequireValue()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments(@"C:\outside\file.md")]
    [Arguments(@"..\outside\file.md")]
    [Arguments(@"..\workspace-copy\file.md")]
    public async Task NormalizeProjectRelativePath_WhenPathIsNotContained_ReturnsFailure(
        string path
    )
    {
        var fileSystem = new MockFileSystem();

        var result = ProjectPath.NormalizeProjectRelativePath(fileSystem, @"C:\workspace", path);

        await Assert.That(result.IsSuccess).IsFalse();
    }
}
