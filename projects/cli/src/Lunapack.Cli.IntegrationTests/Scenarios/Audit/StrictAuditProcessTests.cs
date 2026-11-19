namespace Lunapack.Cli.IntegrationTests.Scenarios.Audit;

[Property("FileSystem", "Real")]
public sealed class StrictAuditProcessTests
{
    [Test]
    public async Task StrictAudit_WhenEveryManagedTargetMatches_ReturnsSuccess()
    {
        using var workspace = await CreateWorkspaceAsync();

        var result = await CliProcess.InvokeAsync(workspace.Path, "audit", "--strict");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains("pack.txt").And.Contains("current");
        await Assert
            .That(result.StandardOutput)
            .Contains("linked/link.txt")
            .And.Contains("link 'linked'");
    }

    [Test]
    public async Task StrictAudit_WhenPackAndLinkTargetsDrift_ReturnsViolationCodeAndReportsAll()
    {
        using var workspace = await CreateWorkspaceAsync();
        File.Delete(Path.Combine(workspace.Path, "pack.txt"));
        File.WriteAllText(Path.Combine(workspace.Path, "linked", "link.txt"), "modified");

        var result = await CliProcess.InvokeAsync(workspace.Path, "audit", "--strict");

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.StandardOutput).Contains("pack 'example@1.0.0'");
        await Assert.That(result.StandardOutput).Contains("pack.txt").And.Contains("missing");
        await Assert.That(result.StandardOutput).Contains("link 'linked'");
        await Assert
            .That(result.StandardOutput)
            .Contains("linked/link.txt")
            .And.Contains("modified");
    }

    [Test]
    public async Task StrictAudit_WhenFailFastFindsPackViolation_DoesNotInspectLinkTarget()
    {
        using var workspace = await CreateWorkspaceAsync();
        File.WriteAllText(Path.Combine(workspace.Path, "pack.txt"), "modified");
        File.WriteAllText(Path.Combine(workspace.Path, "linked", "link.txt"), "modified");

        var result = await CliProcess.InvokeAsync(
            workspace.Path,
            "audit",
            "--strict",
            "--fail-fast"
        );

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.StandardOutput).Contains("pack.txt").And.Contains("modified");
        await Assert.That(result.StandardOutput).DoesNotContain("linked/link.txt");
    }

    [Test]
    public async Task StrictAudit_WhenFailFastOmitsStrictMode_ReturnsUsageError()
    {
        using var workspace = new TestWorkspace();

        var result = await CliProcess.InvokeAsync(workspace.Path, "audit", "--fail-fast");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).Contains("requires '--strict'");
    }

    [Test]
    public async Task Audit_WhenTargetDriftsWithoutStrictMode_PreservesReportOnlyExit()
    {
        using var workspace = await CreateWorkspaceAsync();
        File.WriteAllText(Path.Combine(workspace.Path, "pack.txt"), "modified");

        var result = await CliProcess.InvokeAsync(workspace.Path, "audit");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains("Resolved packs");
    }

    [Test]
    public async Task StrictAudit_WhenLockFileCannotBeRead_ReturnsOperationError()
    {
        using var workspace = await CreateWorkspaceAsync();
        File.WriteAllText(Path.Combine(workspace.Path, "lunapack-lock.yml"), "schemaVersion: [");

        var result = await CliProcess.InvokeAsync(workspace.Path, "audit", "--strict");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).Contains("Unable to read lunapack-lock.yml");
    }

    [Test]
    public async Task StrictAudit_WhenLockCollectionsAreNull_ReturnsOperationError()
    {
        using var workspace = await CreateWorkspaceAsync();
        File.WriteAllText(
            Path.Combine(workspace.Path, "lunapack-lock.yml"),
            "schemaVersion: 1\npacks:\nlinks:\n"
        );

        var result = await CliProcess.InvokeAsync(workspace.Path, "audit", "--strict");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).Contains("Invalid lunapack-lock.yml");
    }

    [Test]
    public async Task StrictAudit_WhenTargetIsDriveRelative_ReturnsOperationError()
    {
        using var workspace = await CreateWorkspaceAsync();
        var lockPath = Path.Combine(workspace.Path, "lunapack-lock.yml");
        var lockContents = File.ReadAllText(lockPath);
        File.WriteAllText(
            lockPath,
            lockContents.Replace(
                "targetPath: pack.txt",
                "targetPath: 'Z:outside.txt'",
                StringComparison.Ordinal
            )
        );

        var result = await CliProcess.InvokeAsync(workspace.Path, "audit", "--strict");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).Contains("Invalid lunapack-lock.yml");
    }

    private static async Task<TestWorkspace> CreateWorkspaceAsync()
    {
        var workspace = new TestWorkspace();
        try
        {
            var catalog = Directory.CreateDirectory(Path.Combine(workspace.Path, "catalog"));
            var pack = Directory.CreateDirectory(Path.Combine(catalog.FullName, "example"));
            var templates = Directory.CreateDirectory(Path.Combine(pack.FullName, "templates"));
            File.WriteAllText(Path.Combine(templates.FullName, "pack.txt"), "pack content");
            File.WriteAllText(
                Path.Combine(pack.FullName, "pack.yml"),
                "id: example\nversion: 1.0.0\nlicense: MIT\nauthor: LunaPack Tests\nmanagedFiles:\n  - source: templates/pack.txt\n    target: pack.txt\n"
            );
            var linkSource = Directory.CreateDirectory(Path.Combine(workspace.Path, "link-source"));
            File.WriteAllText(Path.Combine(linkSource.FullName, "link.txt"), "link content");

            RequireSuccess(await CliProcess.InvokeAsync(workspace.Path, "init"), "initialize");
            RequireSuccess(
                await CliProcess.InvokeAsync(
                    workspace.Path,
                    "sources",
                    "add",
                    "local",
                    "catalog",
                    "catalog"
                ),
                "add pack source"
            );
            RequireSuccess(
                await CliProcess.InvokeAsync(
                    workspace.Path,
                    "sources",
                    "add",
                    "local",
                    "links",
                    "link-source"
                ),
                "add link source"
            );
            RequireSuccess(
                await CliProcess.InvokeAsync(workspace.Path, "install", "example"),
                "install pack"
            );
            RequireSuccess(
                await CliProcess.InvokeAsync(
                    workspace.Path,
                    "links",
                    "add",
                    "linked",
                    "--source",
                    "links",
                    "--include",
                    "link.txt",
                    "--target",
                    "linked",
                    "--install"
                ),
                "install link"
            );
            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static void RequireSuccess(CliResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to {operation}: {result.StandardOutput}{result.StandardError}"
            );
        }
    }
}
