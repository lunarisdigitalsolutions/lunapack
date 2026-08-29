using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests.Sources;

public sealed class ExternalSourceIdentityTests
{
    [Test]
    [Arguments("https://example.test/owner/packs.git", "example.test/owner/packs")]
    [Arguments("https://Example.TEST/Owner/Packs/", "example.test/Owner/Packs")]
    [Arguments("ssh://git@example.test/owner/packs.git", "example.test/owner/packs")]
    [Arguments("git@example.test:owner/packs.git", "example.test/owner/packs")]
    [Arguments("https://GitHub.com/Owner/Packs.git", "github.com/owner/packs")]
    [Arguments("git@github.com:Owner/Packs.git", "github.com/owner/packs")]
    public async Task Normalizer_WhenTransportsAreEquivalent_ProducesSameIdentity(
        string url,
        string expectedIdentity
    )
    {
        var fingerprint = SourceIdentityNormalizer.CreateGit(url, reference: null, path: null);

        await Assert.That(fingerprint.IsSuccess).IsTrue();
        await Assert.That(fingerprint.Value?.Identity).IsEqualTo(expectedIdentity);
    }

    [Test]
    public async Task Normalizer_WhenRefAndPathSupplied_BuildsCanonicalFingerprint()
    {
        var fingerprint = SourceIdentityNormalizer.CreateGit(
            "https://example.test/owner/packs.git",
            "refs/heads/main",
            @"\packs\catalog\"
        );

        await Assert
            .That(fingerprint.Value?.Value)
            .IsEqualTo("git:example.test/owner/packs@refs/heads/main#/packs/catalog");
    }

    [Test]
    public async Task Normalizer_WhenBasePathOmitted_UsesRepositoryRoot()
    {
        var fingerprint = SourceIdentityNormalizer.CreateGit(
            "https://example.test/owner/packs.git",
            reference: null,
            path: "   "
        );

        await Assert.That(fingerprint.Value?.Path).IsEqualTo("/");
        await Assert.That(fingerprint.Value?.Value).IsEqualTo("git:example.test/owner/packs@#/");
    }

    [Test]
    public async Task Normalizer_WhenUrlEmbedsCredentials_IsRejected()
    {
        var url = string.Concat(
            "https://user",
            ":",
            "placeholder",
            "@example.test/owner/packs.git"
        );

        var fingerprint = SourceIdentityNormalizer.CreateGit(url, reference: null, path: null);

        await Assert.That(fingerprint.IsSuccess).IsFalse();
        await Assert.That(fingerprint.Error).IsNotNull();
    }

    [Test]
    [Arguments("../escape")]
    [Arguments("packs/../../escape")]
    public async Task Normalizer_WhenBasePathEscapes_IsRejected(string basePath)
    {
        var fingerprint = SourceIdentityNormalizer.CreateGit(
            "https://example.test/owner/packs.git",
            reference: null,
            basePath
        );

        await Assert.That(fingerprint.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Normalizer_WhenSourceIsLocal_UsesLocalScheme()
    {
        var fingerprint = SourceIdentityNormalizer.Create(
            new ProjectConfiguration.LocalSource { Name = "local", Path = @"packs\catalog" }
        );

        await Assert.That(fingerprint.Value?.Type).IsEqualTo(SourceFingerprint.LocalType);
        await Assert.That(fingerprint.Value?.Value).IsEqualTo("local:packs/catalog");
    }

    [Test]
    public async Task Normalizer_WhenGitRepositoryIsPosixAbsolutePath_UsesFileIdentity()
    {
        var normalized = SourceIdentityNormalizer.NormalizeRepository("/tmp/engineering-packs/");

        await Assert.That(normalized.IsSuccess).IsTrue();
        await Assert.That(normalized.Value).IsEqualTo("file:///tmp/engineering-packs");
    }

    [Test]
    public async Task Normalizer_WhenBasePathsDiffer_ProducesDistinctFingerprints()
    {
        var first = SourceIdentityNormalizer.CreateGit(
            "https://example.test/owner/packs.git",
            "refs/heads/main",
            "packs"
        );
        var second = SourceIdentityNormalizer.CreateGit(
            "https://example.test/owner/packs.git",
            "refs/heads/main",
            "templates"
        );

        await Assert.That(first.Value?.Value).IsNotEqualTo(second.Value?.Value);
    }

    [Test]
    [Arguments("git@github.com:Owner/Packs.git", "github.com/owner/packs")]
    [Arguments("https://github.com/Owner/Packs.git", "github.com/owner/packs")]
    public async Task Normalizer_WhenRepositoryProvided_NormalizesRepository(
        string repository,
        string expected
    )
    {
        var normalized = SourceIdentityNormalizer.NormalizeRepository(repository);

        await Assert.That(normalized.Value).IsEqualTo(expected);
    }

    [Test]
    public async Task RefParser_WhenBranchAndTagShareName_ReportsBothCandidates()
    {
        var refs = GitRefResolver.ParseCanonicalRefs(
            string.Join(
                '\n',
                "1111111111111111111111111111111111111111\trefs/heads/main",
                "2222222222222222222222222222222222222222\trefs/tags/main"
            )
        );

        await Assert.That(refs.Count).IsEqualTo(2);
        await Assert.That(refs.ContainsKey("refs/heads/main")).IsTrue();
        await Assert.That(refs.ContainsKey("refs/tags/main")).IsTrue();
    }

    [Test]
    public async Task RefParser_WhenTagIsAnnotated_PrefersPeeledCommit()
    {
        var refs = GitRefResolver.ParseCanonicalRefs(
            string.Join(
                '\n',
                "1111111111111111111111111111111111111111\trefs/tags/v1.0.0",
                "2222222222222222222222222222222222222222\trefs/tags/v1.0.0^{}"
            )
        );

        await Assert
            .That(refs["refs/tags/v1.0.0"])
            .IsEqualTo("2222222222222222222222222222222222222222");
    }

    [Test]
    public async Task CacheIdentity_WhenTransportsAreEquivalent_SharesFingerprint()
    {
        var https = GitSourceCacheIdentity.Create(
            new ProjectConfiguration.GitSource
            {
                Name = "https",
                Url = "https://example.test/owner/packs.git",
                Ref = "refs/heads/main",
            }
        );
        var ssh = GitSourceCacheIdentity.Create(
            new ProjectConfiguration.GitSource
            {
                Name = "ssh",
                Url = "git@example.test:owner/packs.git",
                Ref = "refs/heads/main",
            }
        );

        await Assert.That(https.Fingerprint).IsEqualTo(ssh.Fingerprint);
    }

    [Test]
    public async Task Configuration_WhenSourcesShareFingerprint_IsRejected()
    {
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Sources =
            [
                new ProjectConfiguration.GitSource
                {
                    Name = "https",
                    Url = "https://example.test/owner/packs.git",
                    Ref = "refs/heads/main",
                },
                new ProjectConfiguration.GitSource
                {
                    Name = "ssh",
                    Url = "git@example.test:owner/packs.git",
                    Ref = "refs/heads/main",
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert
            .That(issues.Any(issue => issue.Contains("https", StringComparison.Ordinal)))
            .IsTrue();
        await Assert
            .That(issues.Any(issue => issue.Contains("ssh", StringComparison.Ordinal)))
            .IsTrue();
    }
}
