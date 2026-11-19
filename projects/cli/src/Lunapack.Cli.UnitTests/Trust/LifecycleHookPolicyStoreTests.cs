using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.UnitTests.Trust;

public sealed class LifecycleHookPolicyStoreTests
{
    [Test]
    public async Task Scenario_SidecarMissing_UsesDefaultPolicy()
    {
        var (fileSystem, store) = CreateStore();

        var loaded = await store.LoadAsync();

        await Assert.That(loaded.IsSuccess).IsTrue();
        await Assert.That(loaded.RequireValue().Global.AllowedEnvironmentVariables).IsEmpty();
        await Assert.That(loaded.RequireValue().Global.TimeoutSeconds).IsNull();
        await Assert.That(loaded.RequireValue().Projects).IsEmpty();
        await Assert
            .That(fileSystem.Path.GetFileName(store.SettingsPath))
            .IsEqualTo(LifecycleHookPolicyStore.FileName);
    }

    [Test]
    public async Task Scenario_PolicyIsInvalid_RejectsWithoutWriting()
    {
        var (fileSystem, store) = CreateStore();
        var settings = new LifecycleHookPolicySettings
        {
            Global = new LifecycleHookPolicy { TimeoutSeconds = 1801 },
        };

        var saved = await store.SaveAsync(settings);

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert.That(fileSystem.File.Exists(store.SettingsPath)).IsFalse();
    }

    [Test]
    public async Task Scenario_ProjectKeyIsNotCanonical_RejectsSettings()
    {
        var (fileSystem, store) = CreateStore();
        var projectDirectory = fileSystem.Path.Combine("projects", "project");
        fileSystem.AddDirectory(projectDirectory);
        var settings = new LifecycleHookPolicySettings
        {
            Projects =
            {
                [
                    fileSystem.Path.GetFullPath(projectDirectory)
                        + fileSystem.Path.DirectorySeparatorChar
                ] = new LifecycleHookPolicy { TimeoutSeconds = 30 },
            },
        };

        var saved = await store.SaveAsync(settings);

        await Assert.That(saved.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Scenario_SettingsDirectoryIsReparsePoint_ReturnsFailure()
    {
        var (fileSystem, store) = CreateStore();
        var settingsDirectory = fileSystem
            .Path.GetDirectoryName(store.SettingsPath)
            .RequireNotNull();
        fileSystem.AddDirectory(settingsDirectory);
        fileSystem.File.SetAttributes(
            settingsDirectory,
            FileAttributes.Directory | FileAttributes.ReparsePoint
        );

        var loaded = await store.LoadAsync();

        await Assert.That(loaded.IsSuccess).IsFalse();
        await Assert.That(loaded.Error).Contains("link or reparse point");
    }

    private static (MockFileSystem FileSystem, LifecycleHookPolicyStore Store) CreateStore()
    {
        var fileSystem = new MockFileSystem();
        var settingsDirectory = fileSystem.Path.Combine("profile", UserSettingsStore.DirectoryName);
        return (fileSystem, new LifecycleHookPolicyStore(fileSystem, settingsDirectory));
    }
}
