using System.IO.Abstractions;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.UnitTests;

public sealed class TrustPolicyTests
{
    [Test]
    public async Task IsTrusted_WhenProjectSourceDeclarationAcknowledged_ReturnsTrue()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var fixture = CreateFixture(workspace.Path, "source");

        var trusted = fixture.Policy.IsTrusted(
            workspace.Path,
            fixture.ProjectKey,
            fixture.Configuration,
            fixture.Settings,
            "local",
            fixture.Identity,
            "sdk"
        );

        await Assert.That(trusted).IsTrue();
    }

    [Test]
    public async Task IsTrusted_WhenSourceNameRebound_ReturnsFalse()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "other-source"));
        var fixture = CreateFixture(workspace.Path, "source");
        fixture.Configuration.Sources[0] = new ProjectConfiguration.LocalSource
        {
            Name = "local",
            Path = "other-source",
        };
        var reboundIdentity = ConfiguredSourceIdentity
            .CreateForTrust(new FileSystem(), workspace.Path, fixture.Configuration.Sources[0])
            .RequireValue();

        var trusted = fixture.Policy.IsTrusted(
            workspace.Path,
            fixture.ProjectKey,
            fixture.Configuration,
            fixture.Settings,
            "local",
            reboundIdentity,
            "sdk"
        );

        await Assert.That(trusted).IsFalse();
    }

    [Test]
    public async Task IsTrusted_WhenProjectKeyDiffers_ReturnsFalse()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var copiedProject = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "copied-project"))
            .FullName;
        Directory.CreateDirectory(Path.Combine(copiedProject, "source"));
        var fixture = CreateFixture(workspace.Path, "source");
        var copiedIdentity = ConfiguredSourceIdentity
            .CreateForTrust(new FileSystem(), copiedProject, fixture.Configuration.Sources[0])
            .RequireValue();
        var copiedKey = CanonicalProjectPath
            .Resolve(new FileSystem(), copiedProject)
            .RequireValue();

        var trusted = fixture.Policy.IsTrusted(
            copiedProject,
            copiedKey,
            fixture.Configuration,
            fixture.Settings,
            "local",
            copiedIdentity,
            "sdk"
        );

        await Assert.That(trusted).IsFalse();
    }

    [Test]
    public async Task IsTrusted_WhenPackAcknowledgementUsesAnotherId_ReturnsFalse()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var fixture = CreateFixture(workspace.Path, "source", trustSource: false);

        var trusted = fixture.Policy.IsTrusted(
            workspace.Path,
            fixture.ProjectKey,
            fixture.Configuration,
            fixture.Settings,
            "local",
            fixture.Identity,
            "quality"
        );

        await Assert.That(trusted).IsFalse();
    }

    [Test]
    public async Task IsTrusted_WhenProjectDeclarationHasNoUserAcknowledgement_ReturnsFalse()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var fixture = CreateFixture(workspace.Path, "source");
        fixture.Settings.Projects.Clear();

        var trusted = fixture.Policy.IsTrusted(
            workspace.Path,
            fixture.ProjectKey,
            fixture.Configuration,
            fixture.Settings,
            "local",
            fixture.Identity,
            "sdk"
        );

        await Assert.That(trusted).IsFalse();
    }

    [Test]
    public async Task IsTrusted_WhenLocalTrustBelongsToAnotherProject_ReturnsFalseUntilGlobal()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var otherProject = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "other-project"))
            .FullName;
        Directory.CreateDirectory(Path.Combine(otherProject, "source"));
        var fixture = CreateFixture(workspace.Path, "source");
        fixture.Configuration.Trust.Sources.Clear();
        fixture.Settings.Projects[fixture.ProjectKey].Sources.Add(fixture.Identity);
        var otherIdentity = ConfiguredSourceIdentity
            .CreateForTrust(new FileSystem(), otherProject, fixture.Configuration.Sources[0])
            .RequireValue();
        var otherKey = CanonicalProjectPath.Resolve(new FileSystem(), otherProject).RequireValue();

        var localTrusted = fixture.Policy.IsTrusted(
            otherProject,
            otherKey,
            fixture.Configuration,
            fixture.Settings,
            "local",
            otherIdentity,
            "sdk"
        );
        fixture.Settings.Global.Sources.Add(otherIdentity);
        var globalTrusted = fixture.Policy.IsTrusted(
            otherProject,
            otherKey,
            fixture.Configuration,
            fixture.Settings,
            "local",
            otherIdentity,
            "sdk"
        );

        await Assert.That(localTrusted).IsFalse();
        await Assert.That(globalTrusted).IsTrue();
    }

    private static TrustFixture CreateFixture(
        string projectDirectory,
        string sourcePath,
        bool trustSource = true
    )
    {
        var fileSystem = new FileSystem();
        var source = new ProjectConfiguration.LocalSource { Name = "local", Path = sourcePath };
        var identity = ConfiguredSourceIdentity
            .CreateForTrust(fileSystem, projectDirectory, source)
            .RequireValue();
        var projectKey = CanonicalProjectPath.Resolve(fileSystem, projectDirectory).RequireValue();
        var configuration = new ProjectConfiguration
        {
            Sources = [source],
            Trust = new ProjectConfiguration.ProjectTrust
            {
                Sources = trustSource ? ["local"] : [],
                Packs = trustSource
                    ? []
                    : [new ProjectConfiguration.TrustedPack { Id = "sdk", Source = "local" }],
            },
        };
        var settings = new UserSettings
        {
            Projects =
            {
                [projectKey] = new LocalProjectTrust
                {
                    Acknowledgements = new TrustAcknowledgements
                    {
                        Sources = trustSource ? [identity] : [],
                        Packs = trustSource
                            ? []
                            : [new TrustedPackIdentity { Id = "sdk", Source = identity }],
                    },
                },
            },
        };
        return new TrustFixture(
            new TrustPolicy(fileSystem),
            projectKey,
            configuration,
            settings,
            identity
        );
    }

    private sealed record TrustFixture(
        TrustPolicy Policy,
        string ProjectKey,
        ProjectConfiguration Configuration,
        UserSettings Settings,
        ConfiguredSourceIdentity Identity
    );
}
