using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.UnitTests.Trust;

public sealed class UserSettingsStoreTests
{
    [Test]
    public async Task SaveAndLoad_WhenDenialConfigured_PreservesScopesAndGrants()
    {
        using var workspace = new TestWorkspace();
        var projectDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "project"))
            .FullName;
        var projectKey = CanonicalProjectPath
            .Resolve(new FileSystem(), projectDirectory)
            .RequireValue();
        var source = ConfiguredSourceIdentity.CreateLocal(projectDirectory);
        var store = new UserSettingsStore(new FileSystem(), workspace.Path);
        var settings = new UserSettings
        {
            Global = new UserTrust
            {
                Deny = new ScriptDenial { Scripts = true },
                Sources = [source],
            },
            Projects =
            {
                [projectKey] = new LocalProjectTrust
                {
                    Deny = new ScriptDenial { Scripts = true },
                    Packs = [new TrustedPackIdentity { Id = "example", Source = source }],
                },
            },
        };

        var saved = await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert.That(loaded.RequireValue().Global.Deny?.Scripts).IsTrue();
        await Assert.That(loaded.RequireValue().Global.Sources).Contains(source);
        await Assert.That(loaded.RequireValue().Projects[projectKey].Deny?.Scripts).IsTrue();
        await Assert
            .That(loaded.RequireValue().Projects[projectKey].Packs.Single().Id)
            .IsEqualTo("example");
    }

    [Test]
    public async Task Load_WhenDenialOmitted_DefaultsToNotDenied()
    {
        using var workspace = new TestWorkspace();
        var settingsDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, UserSettingsStore.DirectoryName))
            .FullName;
        UserSettingsPathSecurity.Apply(settingsDirectory, directory: true);
        var settingsPath = Path.Combine(settingsDirectory, UserSettingsStore.FileName);
        File.WriteAllText(settingsPath, "global: {}\n");
        UserSettingsPathSecurity.Apply(settingsPath, directory: false);
        var store = new UserSettingsStore(new FileSystem(), workspace.Path);

        var loaded = await store.LoadAsync();

        await Assert.That(loaded.IsSuccess).IsTrue();
        await Assert.That(loaded.RequireValue().Global.Deny?.Scripts == true).IsFalse();
        await Assert.That(loaded.RequireValue().Global.Sources).IsEmpty();
        await Assert.That(loaded.RequireValue().Global.Packs).IsEmpty();
    }

    [Test]
    public async Task Load_WhenAcknowledgementsContainDenial_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        var projectDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "project"))
            .FullName;
        var projectKey = ProjectPath.Normalize(projectDirectory);
        var settingsDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, UserSettingsStore.DirectoryName))
            .FullName;
        UserSettingsPathSecurity.Apply(settingsDirectory, directory: true);
        var settingsPath = Path.Combine(settingsDirectory, UserSettingsStore.FileName);
        File.WriteAllText(
            settingsPath,
            $"projects:\n  '{projectKey}':\n    acknowledgements:\n      deny:\n        scripts: true\n"
        );
        UserSettingsPathSecurity.Apply(settingsPath, directory: false);
        var store = new UserSettingsStore(new FileSystem(), workspace.Path);

        var loaded = await store.LoadAsync();

        await Assert.That(loaded.IsSuccess).IsFalse();
    }

    [Test]
    public async Task SaveAndLoad_WhenSettingsValid_UsesPrivateProfilePath()
    {
        using var workspace = new TestWorkspace();
        var store = new UserSettingsStore(new FileSystem(), workspace.Path);
        var settings = new UserSettings
        {
            Global = new UserTrust
            {
                Sources =
                [
                    ConfiguredSourceIdentity.CreateGit(
                        "https://example.test/packs.git",
                        "main",
                        "packs"
                    ),
                ],
            },
        };

        var saved = await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert.That(loaded.IsSuccess).IsTrue();
        await Assert.That(File.Exists(store.SettingsPath)).IsTrue();
        await Assert
            .That(loaded.RequireValue().Global.Sources)
            .IsEquivalentTo(settings.Global.Sources);
        await Assert
            .That(Path.GetDirectoryName(store.SettingsPath))
            .IsEqualTo(Path.Combine(workspace.Path, UserSettingsStore.DirectoryName));
        await Assert
            .That(
                UserSettingsPathSecurity.ValidateExisting(
                    new FileSystem(),
                    Path.GetDirectoryName(store.SettingsPath).RequireNotNull(),
                    directory: true
                )
            )
            .IsNull();
        await Assert
            .That(
                UserSettingsPathSecurity.ValidateExisting(
                    new FileSystem(),
                    store.SettingsPath,
                    directory: false
                )
            )
            .IsNull();
    }

    [Test]
    public async Task Constructor_WhenProfileNotSupplied_UsesOperatingSystemUserProfile()
    {
        var store = new UserSettingsStore(new FileSystem());

        await Assert
            .That(store.SettingsPath)
            .IsEqualTo(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    UserSettingsStore.DirectoryName,
                    UserSettingsStore.FileName
                )
            );
    }

    [Test]
    public async Task Load_WhenSettingsContainUnknownProperty_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        var settingsDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, UserSettingsStore.DirectoryName))
            .FullName;
        UserSettingsPathSecurity.Apply(settingsDirectory, directory: true);
        var settingsPath = Path.Combine(settingsDirectory, UserSettingsStore.FileName);
        File.WriteAllText(settingsPath, "unknown: true\n");
        UserSettingsPathSecurity.Apply(settingsPath, directory: false);
        var store = new UserSettingsStore(new FileSystem(), workspace.Path);

        var loaded = await store.LoadAsync();

        await Assert.That(loaded.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Load_WhenSettingsDirectoryIsReparsePoint_ReturnsFailure()
    {
        var fileSystem = new MockFileSystem();
        const string profileDirectory = @"C:\profile";
        var settingsDirectory = fileSystem.Path.Combine(
            profileDirectory,
            UserSettingsStore.DirectoryName
        );
        fileSystem.AddDirectory(settingsDirectory);
        fileSystem.File.SetAttributes(
            settingsDirectory,
            FileAttributes.Directory | FileAttributes.ReparsePoint
        );
        var store = new UserSettingsStore(fileSystem, profileDirectory);

        var loaded = await store.LoadAsync();

        await Assert.That(loaded.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Load_WhenConfigPathIsDirectory_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        var settingsDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, UserSettingsStore.DirectoryName))
            .FullName;
        UserSettingsPathSecurity.Apply(settingsDirectory, directory: true);
        var configDirectory = Directory
            .CreateDirectory(Path.Combine(settingsDirectory, UserSettingsStore.FileName))
            .FullName;
        UserSettingsPathSecurity.Apply(configDirectory, directory: true);
        var store = new UserSettingsStore(new FileSystem(), workspace.Path);

        var loaded = await store.LoadAsync();

        await Assert.That(loaded.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Save_WhenProjectKeyUsesAlias_PreservesExistingSettings()
    {
        using var workspace = new TestWorkspace();
        var projectDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "project"))
            .FullName;
        var store = new UserSettingsStore(new FileSystem(), workspace.Path);
        await Assert.That((await store.SaveAsync(new UserSettings())).IsSuccess).IsTrue();
        var initialContents = File.ReadAllText(store.SettingsPath);
        var settings = new UserSettings
        {
            Projects =
            {
                [projectDirectory + Path.DirectorySeparatorChar] = new LocalProjectTrust(),
            },
        };

        var saved = await store.SaveAsync(settings);

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert.That(File.ReadAllText(store.SettingsPath)).IsEqualTo(initialContents);
        await Assert
            .That(
                Directory.GetFiles(
                    Path.GetDirectoryName(store.SettingsPath).RequireNotNull(),
                    "*.tmp"
                )
            )
            .IsEmpty();
    }

    [Test]
    public async Task GetProjectKey_WhenPathContainsDotSegments_ReturnsCanonicalPhysicalPath()
    {
        using var workspace = new TestWorkspace();
        var projectDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "project"))
            .FullName;
        var store = new UserSettingsStore(new FileSystem(), workspace.Path);

        var key = store.GetProjectKey(Path.Combine(projectDirectory, "child", "..", "."));

        await Assert.That(key.IsSuccess).IsTrue();
        await Assert
            .That(key.RequireValue())
            .IsEqualTo(
                CanonicalProjectPath.Resolve(new FileSystem(), projectDirectory).RequireValue()
            );
    }

    [Test]
    public async Task GetProjectKey_WhenPathIsSymbolicLink_ReturnsPhysicalTarget()
    {
        using var workspace = new TestWorkspace();
        var projectDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "project"))
            .FullName;
        var alias = Path.Combine(workspace.Path, "project-alias");
        try
        {
            Directory.CreateSymbolicLink(alias, projectDirectory);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return;
        }
        var store = new UserSettingsStore(new FileSystem(), workspace.Path);

        var key = store.GetProjectKey(alias);

        await Assert.That(key.IsSuccess).IsTrue();
        await Assert.That(key.RequireValue()).IsEqualTo(ProjectPath.Normalize(projectDirectory));
    }
}
