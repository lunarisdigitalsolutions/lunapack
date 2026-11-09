using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests.Sources.Git;

public sealed class GitProcessRunnerTests
{
    [Test]
    [Arguments(
        "fatal: Authentication failed for 'https://user:sentinel@registry.example.test/packs.git'",
        "Git authentication failed"
    )]
    [Arguments(
        "fatal: could not read Username for 'https://registry.example.test': terminal prompts disabled",
        "Git authentication failed"
    )]
    [Arguments("Permission denied (publickey).", "Git access was denied")]
    [Arguments(
        "fatal: unable to access 'https://registry.example.test/packs.git': Could not resolve host",
        "Git host is unavailable"
    )]
    [Arguments(
        "SSL certificate problem: unable to get local issuer certificate",
        "TLS certificate"
    )]
    public async Task CreateExitCodeError_WhenGitReportsFailure_ReturnsRedactedGuidance(
        string diagnostic,
        string expectedGuidance
    )
    {
        var error = GitProcessRunner.CreateExitCodeError(
            128,
            new GitProcessOutput(string.Empty, diagnostic)
        );

        await Assert.That(error).Contains("Git exited with code 128");
        await Assert.That(error).Contains(expectedGuidance);
        await Assert.That(error).DoesNotContain("sentinel");
        await Assert.That(error).DoesNotContain("registry.example.test");
    }
}
