namespace Lunapack.Cli.SecurityTests;

public sealed class GitSourceSecurityTests
{
    [Test]
    [Arguments("file:///outside/repository")]
    [Arguments("ftp://example.test/repository.git")]
    [Arguments("--upload-pack=executable")]
    [Arguments("https://example.test")]
    [Arguments("https://exa mple.test/repository.git")]
    public async Task NormalizeRepository_WhenLocationIsUnsafeOrIncomplete_ReturnsFailure(
        string repository
    )
    {
        var result = SourceIdentityNormalizer.NormalizeRepository(repository);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task NormalizeRepository_WhenUrlEmbedsCredentials_DoesNotExposeCredential()
    {
        const string credential = "placeholder-secret";
        var result = SourceIdentityNormalizer.NormalizeRepository(
            $"https://user:{credential}@example.test/repository.git"
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).DoesNotContain(credential);
    }

    [Test]
    [Arguments("../outside")]
    [Arguments("packs/../../outside")]
    [Arguments("packs\\..\\..\\outside")]
    public async Task CreateGit_WhenRepositoryPathTraversesParent_ReturnsFailure(string path)
    {
        var result = SourceIdentityNormalizer.CreateGit(
            "https://example.test/repository.git",
            reference: null,
            path
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }
}
