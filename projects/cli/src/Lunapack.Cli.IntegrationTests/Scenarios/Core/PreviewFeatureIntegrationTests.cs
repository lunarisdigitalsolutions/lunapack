using System.Text.Json;

namespace Lunapack.Cli.IntegrationTests.Scenarios.Core;

[Property("FileSystem", "Real")]
public sealed class PreviewFeatureIntegrationTests
{
    [Test]
    public async Task PreviewFeatures_WhenCombined_PreserveStateAcrossInstallUpdateAndDiagnostics()
    {
        using var workspace = new TestWorkspace();
        await InitializePreviewProjectAsync(workspace.Path);
        await AddPreviewSourceAsync(workspace.Path);
        var hiddenContentRejected = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "example@1.0.0"
        );
        var installed = await CliProcess.InvokeAsync(
            workspace.Path,
            "install",
            "example@1.0.0",
            "--allow-hidden-characters",
            "--scripts",
            "run"
        );

        await File.WriteAllTextAsync(
            Path.Combine(workspace.Path, ".preview.txt"),
            "locally modified"
        );
        var updated = await CliProcess.InvokeAsync(
            workspace.Path,
            "update",
            "example",
            "--allow-hidden-characters",
            "--scripts",
            "run"
        );
        var strictAudit = await CliProcess.InvokeAsync(workspace.Path, "audit", "--strict");
        var audit = await CliProcess.InvokeAsync(workspace.Path, "audit", "--format", "json");
        var sbom = await CliProcess.InvokeAsync(
            workspace.Path,
            "sbom",
            "--target",
            "cyclonedx-1.6"
        );
        var history = await CliProcess.InvokeAsync(
            workspace.Path,
            "hooks",
            "history",
            "--limit",
            "10"
        );
        var doctor = await CliProcess.InvokeAsync(workspace.Path, "doctor");

        await AssertPreviewResultsAsync(
            workspace.Path,
            hiddenContentRejected,
            installed,
            updated,
            strictAudit,
            audit,
            sbom,
            history,
            doctor
        );
    }

    private static async Task InitializePreviewProjectAsync(string workspacePath)
    {
        await RequireSuccessAsync(workspacePath, "init");
        await RequireSuccessAsync(
            workspacePath,
            "config",
            "set",
            "resource-limits.files-per-pack",
            "100",
            "--project"
        );
        await RequireSuccessAsync(
            workspacePath,
            "config",
            "set",
            "next-steps.enabled",
            "false",
            "--project"
        );
        await RequireSuccessAsync(
            workspacePath,
            "config",
            "set",
            "git.allow-insecure-transport",
            "false",
            "--project"
        );
    }

    private static async Task AddPreviewSourceAsync(string workspacePath)
    {
        var sourcePath = Path.Combine(workspacePath, "preview-source");
        CreatePackRelease(
            sourcePath,
            "example-v1",
            "1.0.0",
            "preInstall",
            ".preview.txt",
            "version one\u200B"
        );
        CreatePackRelease(
            sourcePath,
            "example-v2",
            "2.0.0",
            "preUpdate",
            ".preview-v2.txt",
            "version two"
        );
        await RequireSuccessAsync(
            workspacePath,
            "sources",
            "add",
            "local",
            "preview",
            "preview-source"
        );
    }

    private static async Task AssertPreviewResultsAsync(
        string workspacePath,
        CliResult hiddenContentRejected,
        CliResult installed,
        CliResult updated,
        CliResult strictAudit,
        CliResult audit,
        CliResult sbom,
        CliResult history,
        CliResult doctor
    )
    {
        await Assert.That(hiddenContentRejected.ExitCode).IsEqualTo(1);
        await Assert.That(hiddenContentRejected.StandardOutput).Contains("hidden");
        await Assert.That(installed.ExitCode).IsEqualTo(0).Because(installed.StandardOutput);
        await Assert.That(updated.ExitCode).IsEqualTo(0).Because(updated.StandardOutput);
        await Assert
            .That(File.ReadAllText(Path.Combine(workspacePath, ".preview.txt")))
            .IsEqualTo("locally modified");
        await Assert
            .That(File.ReadAllText(Path.Combine(workspacePath, ".preview-v2.txt")))
            .IsEqualTo("version two");
        await Assert.That(strictAudit.ExitCode).IsEqualTo(0).Because(strictAudit.StandardOutput);
        await Assert.That(strictAudit.StandardOutput).Contains("current");
        await Assert.That(audit.ExitCode).IsEqualTo(0).Because(audit.StandardOutput);
        using var auditDocument = JsonDocument.Parse(audit.StandardOutput);
        await Assert
            .That(auditDocument.RootElement.GetProperty("command").GetString())
            .IsEqualTo("audit");
        await Assert.That(sbom.ExitCode).IsEqualTo(0).Because(sbom.StandardOutput);
        using var sbomDocument = JsonDocument.Parse(sbom.StandardOutput);
        await Assert
            .That(sbomDocument.RootElement.GetProperty("bomFormat").GetString())
            .IsEqualTo("CycloneDX");
        await Assert.That(history.ExitCode).IsEqualTo(0).Because(history.StandardOutput);
        await Assert.That(history.StandardOutput).Contains("example@1.0.0");
        await Assert.That(history.StandardOutput).Contains("example@2.0.0");
        await Assert.That(doctor.ExitCode).IsEqualTo(0).Because(doctor.StandardOutput);
    }

    private static void CreatePackRelease(
        string sourcePath,
        string directoryName,
        string version,
        string hookEvent,
        string managedTarget,
        string managedContent
    )
    {
        var packDirectory = Directory.CreateDirectory(Path.Combine(sourcePath, directoryName));
        var templateDirectory = Directory.CreateDirectory(
            Path.Combine(packDirectory.FullName, "templates")
        );
        File.WriteAllText(Path.Combine(templateDirectory.FullName, "content.txt"), managedContent);
        File.WriteAllText(
            Path.Combine(packDirectory.FullName, "pack.yml"),
            $"id: example\nversion: {version}\nlicense: MIT\nauthor: LunaPack Tests\nhooks:\n  {hookEvent}:\n    - type: script\n      command: dotnet\n      arguments:\n        - --version\nmanagedFiles:\n  - source: templates/content.txt\n    target: {managedTarget}\n"
        );
    }

    private static async Task RequireSuccessAsync(string workspacePath, params string[] arguments)
    {
        var result = await CliProcess.InvokeAsync(workspacePath, arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Luna command '{string.Join(" ", arguments)}' failed: {result.StandardOutput}{result.StandardError}"
            );
        }
    }
}
