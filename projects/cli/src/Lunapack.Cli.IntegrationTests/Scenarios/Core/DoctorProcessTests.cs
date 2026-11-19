namespace Lunapack.Cli.IntegrationTests.Scenarios.Core;

[Property("FileSystem", "Real")]
public sealed class DoctorProcessTests
{
    [Test]
    public async Task Doctor_WhenProjectIsUninitialized_ReportsEnvironmentWithoutCreatingState()
    {
        using var workspace = new TestWorkspace();
        var profileDirectory = Path.Combine(workspace.Path, ".lunapack-test-profile");

        var result = await CliProcess.InvokeAsync(workspace.Path, "doctor");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains("[INFO] Project state");
        await Assert.That(result.StandardOutput).Contains("[PASS] Git");
        await Assert.That(File.Exists(Path.Combine(workspace.Path, "lunapack.yml"))).IsFalse();
        await Assert.That(Directory.Exists(profileDirectory)).IsFalse();
    }

    [Test]
    public async Task Doctor_WhenProjectIsInitialized_PreservesProjectState()
    {
        using var workspace = new TestWorkspace();
        await CliProcess.InvokeAsync(workspace.Path, "init");
        var configurationPath = Path.Combine(workspace.Path, "lunapack.yml");
        var lockPath = Path.Combine(workspace.Path, "lunapack-lock.yml");
        var configuration = File.ReadAllText(configurationPath);
        var lockFile = File.ReadAllText(lockPath);

        var result = await CliProcess.InvokeAsync(workspace.Path, "doctor");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains("[PASS] Project state");
        await Assert.That(File.ReadAllText(configurationPath)).IsEqualTo(configuration);
        await Assert.That(File.ReadAllText(lockPath)).IsEqualTo(lockFile);
    }

    [Test]
    public async Task Doctor_WhenUserSettingsAreInvalid_ReportsFailureWithoutPreferenceBootstrap()
    {
        using var workspace = new TestWorkspace();
        await CliProcess.InvokeAsync(workspace.Path, "init");
        var settingsDirectory = Path.Combine(workspace.Path, ".lunapack-test-profile", ".lunapack");
        Directory.CreateDirectory(settingsDirectory);
        File.WriteAllText(Path.Combine(settingsDirectory, "config.yml"), "invalid: true\n");

        var result = await CliProcess.InvokeAsync(workspace.Path, "doctor");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).Contains("[FAIL] User settings");
        await Assert
            .That(result.StandardOutput)
            .DoesNotContain("Unable to resolve CLI preferences");
    }

    [Test]
    public async Task Doctor_WhenOnlyLocalSourceIsConfigured_SkipsRemoteNetworkChecks()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "packs"));
        await CliProcess.InvokeAsync(workspace.Path, "init");
        var source = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "local",
            "local",
            "packs"
        );

        var result = await CliProcess.InvokeAsync(workspace.Path, "doctor", "--network");

        await Assert.That(source.ExitCode).IsEqualTo(0);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains("No configured remote source");
    }

    [Test]
    public async Task Doctor_WhenGitSourceIsConfiguredWithNetwork_ChecksItAndContinues()
    {
        using var workspace = new TestWorkspace();
        using var repository = new GitTestRepository();
        File.WriteAllText(Path.Combine(repository.Path, "README.md"), "source");
        await GitProcess.InvokeAsync(repository.Path, "init", "--initial-branch=main");
        await GitProcess.InvokeAsync(repository.Path, "add", ".");
        await GitProcess.InvokeAsync(
            repository.Path,
            "-c",
            "user.email=lunapack@example.test",
            "-c",
            "user.name=Lunapack Test",
            "commit",
            "--quiet",
            "-m",
            "Initial source"
        );
        await CliProcess.InvokeAsync(workspace.Path, "init");
        var source = await CliProcess.InvokeAsync(
            workspace.Path,
            "sources",
            "add",
            "git",
            "repository",
            repository.Path,
            "--ref",
            "main"
        );

        var result = await CliProcess.InvokeAsync(workspace.Path, "doctor", "--network");

        await Assert.That(source.ExitCode).IsEqualTo(0);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardOutput);
        await Assert.That(result.StandardOutput).Contains("[PASS] Source repository");
    }

    [Test]
    public async Task Doctor_WhenRemoteSourceFails_ReportsFailureAndStillCompletesOtherChecks()
    {
        using var workspace = new TestWorkspace();
        await CliProcess.InvokeAsync(workspace.Path, "init");
        File.WriteAllText(
            Path.Combine(workspace.Path, "lunapack.yml"),
            "schemaVersion: 1\nsources:\n  - name: unavailable\n    type: git\n    url: https://127.0.0.1:1/packs.git\npacks: []\n"
        );

        var result = await CliProcess.InvokeAsync(workspace.Path, "doctor", "--network");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).Contains("[FAIL] Source unavailable");
        await Assert.That(result.StandardOutput).Contains("[PASS] Credential storage");
    }
}
