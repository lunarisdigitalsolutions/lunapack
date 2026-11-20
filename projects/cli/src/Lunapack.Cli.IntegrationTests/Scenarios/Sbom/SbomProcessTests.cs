using System.Text.Json;

namespace Lunapack.Cli.IntegrationTests.Scenarios.Sbom;

[Property("FileSystem", "Real")]
public sealed class SbomProcessTests
{
    [Test]
    [Arguments("cyclonedx-1.6")]
    [Arguments("cyclonedx-1.7")]
    [Arguments("spdx-2.3")]
    [Arguments("spdx-3.0.1")]
    public async Task Sbom_WhenExportingToStdout_WritesOnlyTargetJson(string target)
    {
        using var workspace = new TestWorkspace();
        var initialized = await CliProcess.InvokeAsync(workspace.Path, "init");
        var result = await CliProcess.InvokeAsync(
            workspace.Path,
            "sbom",
            "--target",
            target,
            "--log-level",
            "debug"
        );

        await Assert.That(initialized.ExitCode).IsEqualTo(0);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardError).IsEmpty();
        using var document = JsonDocument.Parse(result.StandardOutput);
        await Assert.That(document.RootElement.ValueKind).IsEqualTo(JsonValueKind.Object);
    }

    [Test]
    public async Task Sbom_WhenTargetIsUnsupported_ReportsOnlyToStandardError()
    {
        using var workspace = new TestWorkspace();
        var result = await CliProcess.InvokeAsync(
            workspace.Path,
            "sbom",
            "--target",
            "unsupported"
        );

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.StandardError).Contains("Unsupported SBOM target");
    }

    [Test]
    public async Task Sbom_WhenLogLevelIsInvalid_ReportsOnlyToStandardError()
    {
        using var workspace = new TestWorkspace();
        var result = await CliProcess.InvokeAsync(
            workspace.Path,
            "sbom",
            "--target",
            "cyclonedx-1.6",
            "--log-level",
            "unsupported"
        );

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.StandardError).Contains("Log level must be");
    }
}
