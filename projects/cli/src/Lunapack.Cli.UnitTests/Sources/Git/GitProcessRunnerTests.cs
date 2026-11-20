using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests.Sources.Git;

public sealed class GitProcessRunnerTests
{
    [Test]
    [NotInParallel]
    public async Task StartInfo_WhenDoctorModeIsUsed_RemovesInheritedGitSecrets()
    {
        var configCount = Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT");
        var configKey = Environment.GetEnvironmentVariable("GIT_CONFIG_KEY_0");
        var configValue = Environment.GetEnvironmentVariable("GIT_CONFIG_VALUE_0");
        var trace = Environment.GetEnvironmentVariable("GIT_TRACE_CURL");
        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "http.extraHeader");
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "secret-header");
            Environment.SetEnvironmentVariable("GIT_TRACE_CURL", "1");
            var executable = OperatingSystem.IsWindows()
                ? "C:\\Windows\\System32\\cmd.exe"
                : "/bin/sh";

            var startInfo = new GitProcessRunner(
                executable,
                nonInteractive: true
            ).CreateStartInfoForTests(executable);

            await Assert.That(startInfo.Environment).DoesNotContainKey("GIT_CONFIG_COUNT");
            await Assert.That(startInfo.Environment).DoesNotContainKey("GIT_CONFIG_KEY_0");
            await Assert.That(startInfo.Environment).DoesNotContainKey("GIT_CONFIG_VALUE_0");
            await Assert.That(startInfo.Environment).DoesNotContainKey("GIT_TRACE_CURL");
            await Assert.That(startInfo.Environment["GIT_CONFIG_NOSYSTEM"]).IsEqualTo("1");
            await Assert.That(startInfo.Environment["GIT_SSH_COMMAND"]).StartsWith("\"");
            await Assert.That(startInfo.Environment["GIT_SSH_COMMAND"]).DoesNotStartWith("ssh");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", configCount);
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", configKey);
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", configValue);
            Environment.SetEnvironmentVariable("GIT_TRACE_CURL", trace);
        }
    }

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
