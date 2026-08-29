using System.IO.Abstractions.TestingHelpers;

namespace Lunapack.Cli.SecurityTests;

public sealed class ProjectPathSecurityTests
{
    [Test]
    [Arguments("")]
    [Arguments("C:\\outside")]
    [Arguments("D:/outside")]
    [Arguments("\\\\server\\share")]
    [Arguments("//server/share")]
    [Arguments("/outside")]
    [Arguments("../outside")]
    [Arguments("nested/../../outside")]
    public async Task NormalizeProjectRelativePath_WhenPathBreaksBoundary_ReturnsFailure(
        string path
    )
    {
        var result = ProjectPath.NormalizeProjectRelativePath(
            new MockFileSystem(),
            "C:\\project",
            path
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).IsNotNull();
    }

    [Test]
    [Arguments("docs\\guide.md", "docs/guide.md")]
    [Arguments("docs/../README.md", "README.md")]
    [Arguments(".github/workflows/../agents/review.md", ".github/agents/review.md")]
    public async Task NormalizeProjectRelativePath_WhenPathStaysWithinBoundary_ReturnsCanonicalPath(
        string path,
        string expected
    )
    {
        var result = ProjectPath.NormalizeProjectRelativePath(
            new MockFileSystem(),
            "C:\\project",
            path
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert.That(result.Value).IsEqualTo(expected);
    }
}
