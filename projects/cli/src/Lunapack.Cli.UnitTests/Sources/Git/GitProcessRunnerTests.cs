using System.Text;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests.Sources.Git;

public sealed class GitProcessRunnerTests
{
    [Test]
    public async Task RunAsync_WhenStandardOutputExceedsCompiledLimit_RejectsInsteadOfTruncating()
    {
        var runner = new GitProcessRunner(maximumStandardOutputBytes: 1);

        var result = await runner.RunAsync(
            ["--version"],
            TimeSpan.FromSeconds(5),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert
            .That(result.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(result.Error).Contains("Git standard output");
        await Assert.That(result.Error).Contains("1 byte limit");
    }

    [Test]
    public async Task RunBounded_WhenOutputIsAtLimit_AcceptsOutput()
    {
        var runner = new GitProcessRunner();
        var timeout = TimeSpan.FromSeconds(5);
        var output = await runner.RunAsync(["--version"], timeout, CancellationToken.None);
        var byteCount = Encoding.UTF8.GetByteCount(output.RequireValue().StandardOutput);

        var result = await runner.RunBoundedAsync(
            ["--version"],
            timeout,
            byteCount,
            "Git version",
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
    }

    [Test]
    public async Task RunBounded_WhenOutputExceedsLimit_RejectsWithLowerBound()
    {
        var runner = new GitProcessRunner();

        var result = await runner.RunBoundedAsync(
            ["--version"],
            TimeSpan.FromSeconds(5),
            1,
            "Git version",
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("Git version");
        await Assert.That(result.Error).Contains("1 byte limit");
        await Assert.That(result.Error).Contains("at least");
    }

    [Test]
    public async Task RunBoundedRecords_WhenOutputMatchesLimit_AcceptsBoundary()
    {
        var runner = new GitProcessRunner();

        var result = await runner.RunBoundedRecordsAsync(
            ["-c", "demo.one=1", "-c", "demo.two=2", "config", "--get-regexp", "^demo\\."],
            TimeSpan.FromSeconds(5),
            2,
            "Git configuration records",
            (byte)'\n',
            cancellationToken: CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
    }

    [Test]
    public async Task RunBoundedRecords_WhenOutputExceedsLimit_RejectsWithLowerBound()
    {
        var runner = new GitProcessRunner();

        var result = await runner.RunBoundedRecordsAsync(
            ["-c", "demo.one=1", "-c", "demo.two=2", "config", "--get-regexp", "^demo\\."],
            TimeSpan.FromSeconds(5),
            1,
            "Git configuration records",
            (byte)'\n',
            cancellationToken: CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("1 record limit");
        await Assert.That(result.Error).Contains("at least 2 records read");
    }

    [Test]
    public async Task RunBoundedRecords_WhenOutputBytesExceedLimit_RejectsBeforeBufferGrowth()
    {
        var runner = new GitProcessRunner();
        var arguments = new[]
        {
            "-c",
            "demo.one=1",
            "-c",
            "demo.two=2",
            "config",
            "--get-regexp",
            "^demo\\.",
        };
        var timeout = TimeSpan.FromSeconds(5);
        var output = await runner.RunAsync(arguments, timeout, CancellationToken.None);
        var byteCount = Encoding.UTF8.GetByteCount(output.RequireValue().StandardOutput);

        var result = await runner.RunBoundedRecordsAsync(
            arguments,
            timeout,
            2,
            "Git configuration records",
            (byte)'\n',
            byteCount - 1,
            recordByteLimit: long.MaxValue
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains($"{byteCount - 1} byte limit");
        await Assert.That(result.Error).Contains($"at least {byteCount} bytes read");
    }

    [Test]
    public async Task RunBoundedRecords_WhenOneRecordExceedsLimit_RejectsRecord()
    {
        var runner = new GitProcessRunner();
        var result = await runner.RunBoundedRecordsAsync(
            ["-c", "demo.long=1234567890", "config", "--get-regexp", "^demo\\."],
            TimeSpan.FromSeconds(5),
            1,
            "Git configuration record",
            (byte)'\n',
            outputByteLimit: 1024,
            recordByteLimit: 10,
            cancellationToken: CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("record exceeds its 10 byte limit");
        await Assert.That(result.Error).Contains("at least 11 bytes read");
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
