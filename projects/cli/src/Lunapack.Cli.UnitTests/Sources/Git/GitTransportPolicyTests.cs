using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.UnitTests.TestSupport;

namespace Lunapack.Cli.UnitTests.Sources.Git;

public sealed class GitTransportPolicyTests
{
    [Test]
    public async Task AuthorizeAsync_WhenInsecureTransportIsDisabled_DeniesWithoutLeakingCredentials()
    {
        using var workspace = new TestWorkspace();
        var policy = TestGitTransportPolicy.Create(
            workspace.FileSystem,
            workspace.UserSettingsStore
        );

        var result = await policy.AuthorizeAsync(
            workspace.Path,
            "http://username:secret@example.test/packs.git",
            "catalog"
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("git.allow-insecure-transport");
        await Assert.That(result.Error).DoesNotContain("username");
        await Assert.That(result.Error).DoesNotContain("secret");
    }

    [Test]
    public async Task AuthorizeAsync_WhenGlobalPreferenceEnablesInsecureTransport_AllowsHttpAndGit()
    {
        using var workspace = new TestWorkspace();
        var preferences = new CliPreferenceService(
            workspace.FileSystem,
            workspace.StateStore,
            workspace.UserSettingsStore
        );
        var enabled = await preferences.SetAsync(
            workspace.Path,
            "git.allow-insecure-transport",
            "true",
            CliPreferenceService.Scope.Global
        );
        var policy = TestGitTransportPolicy.Create(
            workspace.FileSystem,
            workspace.UserSettingsStore
        );

        var http = await policy.AuthorizeAsync(
            workspace.Path,
            "http://example.test/packs.git",
            "http-source"
        );
        var git = await policy.AuthorizeAsync(
            workspace.Path,
            "git://example.test/packs.git",
            "git-source"
        );

        await Assert.That(enabled.IsSuccess).IsTrue();
        await Assert.That(http.Value).IsEqualTo(GitTransportMode.AllowHttp);
        await Assert.That(git.Value).IsEqualTo(GitTransportMode.AllowGit);
    }

    [Test]
    public async Task AuthorizeAsync_WhenRepositoryUsesScpStyleHostNamedGit_AllowsSsh()
    {
        using var workspace = new TestWorkspace();
        var policy = TestGitTransportPolicy.Create(
            workspace.FileSystem,
            workspace.UserSettingsStore
        );

        var result = await policy.AuthorizeAsync(
            workspace.Path,
            "git:organization/packs.git",
            "catalog"
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(GitTransportMode.SecureOnly);
    }

    [Test]
    public async Task AuthorizeAsync_WhenRepositoryStartsWithGitOption_DeniesBeforeProcess()
    {
        using var workspace = new TestWorkspace();
        var policy = TestGitTransportPolicy.Create(
            workspace.FileSystem,
            workspace.UserSettingsStore
        );

        var result = await policy.AuthorizeAsync(
            workspace.Path,
            "--upload-pack=sh -c 'sentinel'@example.test:packs.git",
            "catalog"
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("must not begin");
        await Assert.That(result.Error).DoesNotContain("sentinel");
    }

    [Test]
    public async Task AuthorizeAsync_WhenRepositoryUsesRemoteHelper_DeniesBeforeProcess()
    {
        using var workspace = new TestWorkspace();
        var policy = TestGitTransportPolicy.Create(
            workspace.FileSystem,
            workspace.UserSettingsStore
        );

        var result = await policy.AuthorizeAsync(
            workspace.Path,
            "ext::sentinel-command",
            "catalog"
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("remote-helper");
        await Assert.That(result.Error).DoesNotContain("sentinel");
    }

    [Test]
    public async Task AuthorizeAsync_WhenRepositoryIsUnc_DeniesBeforeProcess()
    {
        using var workspace = new TestWorkspace();
        var policy = TestGitTransportPolicy.Create(
            workspace.FileSystem,
            workspace.UserSettingsStore
        );

        var result = await policy.AuthorizeAsync(
            workspace.Path,
            "//attacker.example/share/repository.git",
            "catalog"
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("UNC");
    }

    [Test]
    public async Task AuthorizeAsync_WhenSourceIsAuthorized_WarnsOncePerRepository()
    {
        using var workspace = new TestWorkspace();
        var preferences = new CliPreferenceService(
            workspace.FileSystem,
            workspace.StateStore,
            workspace.UserSettingsStore
        );
        var enabled = await preferences.SetAsync(
            workspace.Path,
            "git.allow-insecure-transport",
            "true",
            CliPreferenceService.Scope.Global
        );
        var ansiConsole = new Spectre.Console.Testing.TestConsole();
        var policy = TestGitTransportPolicy.Create(
            workspace.FileSystem,
            workspace.UserSettingsStore,
            new CliConsole(ansiConsole, CliLogLevel.Warning)
        );

        var first = await policy.AuthorizeAsync(
            workspace.Path,
            "http://example.test/packs.git",
            "catalog"
        );
        var second = await policy.AuthorizeAsync(
            workspace.Path,
            "http://example.test/packs.git",
            "renamed-catalog"
        );

        await Assert.That(enabled.IsSuccess).IsTrue();
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(second.IsSuccess).IsTrue();
        await Assert
            .That(ansiConsole.Output.Split("Insecure Git transport").Length - 1)
            .IsEqualTo(1);
        await Assert.That(ansiConsole.Output).Contains("read or modified");
        await Assert.That(ansiConsole.Output).DoesNotContain("http://");
    }
}
