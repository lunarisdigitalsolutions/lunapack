using System.IO.Abstractions;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.SecurityTests.Trust;

public sealed class LifecycleHookPolicyStoreSecurityTests
{
    [Test]
    public async Task Scenario_UpdatingPolicy_PreservesScopesAndLegacySettings()
    {
        await RunInWorkspaceAsync(async workspace =>
        {
            var fileSystem = new FileSystem();
            var settingsDirectory = CreatePrivateSettingsDirectory(workspace);
            var legacySettingsPath = Path.Combine(settingsDirectory, UserSettingsStore.FileName);
            const string legacySettings = "global:\n  deny:\n    scripts: true\n";
            File.WriteAllText(legacySettingsPath, legacySettings);
            var projectDirectory = Directory
                .CreateDirectory(Path.Combine(workspace, "project"))
                .FullName;
            var otherProjectDirectory = Directory
                .CreateDirectory(Path.Combine(workspace, "other-project"))
                .FullName;
            var projectKey = CanonicalProjectPath.Resolve(fileSystem, projectDirectory).Value;
            projectKey = RequireValue(projectKey);
            var otherProjectKey = CanonicalProjectPath
                .Resolve(fileSystem, otherProjectDirectory)
                .Value;
            otherProjectKey = RequireValue(otherProjectKey);
            var store = new LifecycleHookPolicyStore(fileSystem, settingsDirectory);
            var initialSettings = new LifecycleHookPolicySettings
            {
                Global = new LifecycleHookPolicy
                {
                    AllowedEnvironmentVariables = ["GLOBAL_VALUE"],
                    TimeoutSeconds = 120,
                },
                Projects =
                {
                    [projectKey] = new LifecycleHookPolicy
                    {
                        AllowedEnvironmentVariables = ["OLD_VALUE"],
                        TimeoutSeconds = 90,
                    },
                    [otherProjectKey] = new LifecycleHookPolicy
                    {
                        AllowedEnvironmentVariables = ["OTHER_VALUE"],
                        TimeoutSeconds = 60,
                    },
                },
            };
            var initialSave = await store.SaveAsync(initialSettings);
            var settings = RequireValue((await store.LoadAsync()).Value);
            settings.Projects[projectKey] = new LifecycleHookPolicy
            {
                AllowedEnvironmentVariables = ["NEW_VALUE"],
                TimeoutSeconds = 30,
            };

            var updated = await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();
            var persistedContents = File.ReadAllText(store.SettingsPath);

            await Assert.That(initialSave.IsSuccess).IsTrue();
            await Assert.That(updated.IsSuccess).IsTrue();
            await Assert.That(loaded.IsSuccess).IsTrue();
            var loadedSettings = RequireValue(loaded.Value);
            await Assert
                .That(loadedSettings.Global.AllowedEnvironmentVariables)
                .Contains("GLOBAL_VALUE");
            await Assert.That(loadedSettings.Global.TimeoutSeconds).IsEqualTo(120);
            await Assert
                .That(loadedSettings.Projects[projectKey].AllowedEnvironmentVariables)
                .Contains("NEW_VALUE");
            await Assert.That(loadedSettings.Projects[projectKey].TimeoutSeconds).IsEqualTo(30);
            await Assert
                .That(loadedSettings.Projects[otherProjectKey].AllowedEnvironmentVariables)
                .Contains("OTHER_VALUE");
            await Assert.That(persistedContents).DoesNotContain("\\");
            await Assert.That(File.ReadAllText(legacySettingsPath)).IsEqualTo(legacySettings);
        });
    }

    [Test]
    public async Task Scenario_InvalidPolicyValueIsPersisted_ReturnsFailure()
    {
        await RunInWorkspaceAsync(async workspace =>
        {
            var settingsDirectory = CreatePrivateSettingsDirectory(workspace);
            var settingsPath = Path.Combine(settingsDirectory, LifecycleHookPolicyStore.FileName);
            File.WriteAllText(settingsPath, "global:\n  timeoutSeconds: 1801\n");
            UserSettingsPathSecurity.Apply(settingsPath, directory: false);
            var store = new LifecycleHookPolicyStore(new FileSystem(), settingsDirectory);

            var loaded = await store.LoadAsync();

            await Assert.That(loaded.IsSuccess).IsFalse();
        });
    }

    [Test]
    public async Task Scenario_ProjectMapsAreExplicitlyNull_RejectsAllSidecars()
    {
        await RunInWorkspaceAsync(async workspace =>
        {
            var settingsDirectory = CreatePrivateSettingsDirectory(workspace);
            var lifecyclePath = Path.Combine(settingsDirectory, LifecycleHookPolicyStore.FileName);
            var sourceIgnorePath = Path.Combine(settingsDirectory, SourceIgnoreStore.FileName);
            var userSettingsPath = Path.Combine(settingsDirectory, UserSettingsStore.FileName);
            File.WriteAllText(lifecyclePath, "global: {}\nprojects: null\n");
            File.WriteAllText(sourceIgnorePath, "projects: null\n");
            File.WriteAllText(userSettingsPath, "projects: null\n");
            UserSettingsPathSecurity.Apply(lifecyclePath, directory: false);
            UserSettingsPathSecurity.Apply(sourceIgnorePath, directory: false);
            UserSettingsPathSecurity.Apply(userSettingsPath, directory: false);
            var fileSystem = new FileSystem();
            var lifecycleStore = new LifecycleHookPolicyStore(fileSystem, settingsDirectory);
            var sourceIgnoreStore = new SourceIgnoreStore(fileSystem, settingsDirectory);
            var userSettingsStore = new UserSettingsStore(fileSystem, workspace);

            var lifecycle = await lifecycleStore.LoadAsync();
            var sourceIgnore = await sourceIgnoreStore.LoadAsync();
            var userSettings = await userSettingsStore.LoadAsync();

            await Assert.That(lifecycle.IsSuccess).IsFalse();
            await Assert.That(sourceIgnore.IsSuccess).IsFalse();
            await Assert.That(userSettings.IsSuccess).IsFalse();
        });
    }

    [Test]
    public async Task Scenario_ProjectKeysContainNul_RejectsAllSidecars()
    {
        await RunInWorkspaceAsync(async workspace =>
        {
            var settingsDirectory = CreatePrivateSettingsDirectory(workspace);
            var lifecyclePath = Path.Combine(settingsDirectory, LifecycleHookPolicyStore.FileName);
            var sourceIgnorePath = Path.Combine(settingsDirectory, SourceIgnoreStore.FileName);
            var userSettingsPath = Path.Combine(settingsDirectory, UserSettingsStore.FileName);
            const string invalidProjectKey = "projects:\n  \"bad\\0key\": {}\n";
            File.WriteAllText(lifecyclePath, invalidProjectKey);
            File.WriteAllText(sourceIgnorePath, invalidProjectKey);
            File.WriteAllText(userSettingsPath, invalidProjectKey);
            UserSettingsPathSecurity.Apply(lifecyclePath, directory: false);
            UserSettingsPathSecurity.Apply(sourceIgnorePath, directory: false);
            UserSettingsPathSecurity.Apply(userSettingsPath, directory: false);
            var fileSystem = new FileSystem();
            var lifecycleStore = new LifecycleHookPolicyStore(fileSystem, settingsDirectory);
            var sourceIgnoreStore = new SourceIgnoreStore(fileSystem, settingsDirectory);
            var userSettingsStore = new UserSettingsStore(fileSystem, workspace);

            var lifecycle = await lifecycleStore.LoadAsync();
            var sourceIgnore = await sourceIgnoreStore.LoadAsync();
            var userSettings = await userSettingsStore.LoadAsync();

            await Assert.That(lifecycle.IsSuccess).IsFalse();
            await Assert.That(sourceIgnore.IsSuccess).IsFalse();
            await Assert.That(userSettings.IsSuccess).IsFalse();
        });
    }

    [Test]
    public async Task Scenario_WindowsLegacyKeyUsesBackslashes_NormalizesPhysicalPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await RunInWorkspaceAsync(async workspace =>
        {
            var fileSystem = new FileSystem();
            var settingsDirectory = CreatePrivateSettingsDirectory(workspace);
            var projectDirectory = Directory
                .CreateDirectory(Path.Combine(workspace, "MixedCaseProject"))
                .FullName;
            var projectKey = CanonicalProjectPath.Resolve(fileSystem, projectDirectory).Value;
            projectKey = RequireValue(projectKey);
            var legacyProjectKey = projectDirectory
                .ToLowerInvariant()
                .Replace(Path.DirectorySeparatorChar, '\\');
            var settingsPath = Path.Combine(settingsDirectory, LifecycleHookPolicyStore.FileName);
            File.WriteAllText(
                settingsPath,
                $"projects:\n  '{legacyProjectKey}':\n    timeoutSeconds: 30\n"
            );
            UserSettingsPathSecurity.Apply(settingsPath, directory: false);
            var store = new LifecycleHookPolicyStore(fileSystem, settingsDirectory);

            var loaded = await store.LoadAsync();

            await Assert.That(loaded.IsSuccess).IsTrue();
            var loadedSettings = RequireValue(loaded.Value);
            await Assert
                .That(loadedSettings.Projects.TryGetValue(projectKey, out var policy))
                .IsTrue();
            await Assert.That(RequireValue(policy).TimeoutSeconds).IsEqualTo(30);
        });
    }

    [Test]
    public async Task Scenario_WindowsLegacyUserSettingsKeyUsesBackslashes_NormalizesKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await RunInWorkspaceAsync(async workspace =>
        {
            var fileSystem = new FileSystem();
            var projectDirectory = Directory
                .CreateDirectory(Path.Combine(workspace, "MixedCaseUserProject"))
                .FullName;
            var store = new UserSettingsStore(fileSystem, workspace);
            var canonicalProjectKey = RequireValue(store.GetProjectKey(projectDirectory).Value);
            var settingsDirectory = RequireValue(Path.GetDirectoryName(store.SettingsPath));
            Directory.CreateDirectory(settingsDirectory);
            UserSettingsPathSecurity.Apply(settingsDirectory, directory: true);
            var legacyProjectKey = projectDirectory
                .ToLowerInvariant()
                .Replace(Path.DirectorySeparatorChar, '\\');
            File.WriteAllText(store.SettingsPath, $"projects:\n  '{legacyProjectKey}': {{}}\n");
            UserSettingsPathSecurity.Apply(store.SettingsPath, directory: false);

            var loaded = await store.LoadAsync();

            await Assert.That(loaded.IsSuccess).IsTrue();
            await Assert
                .That(RequireValue(loaded.Value).Projects.ContainsKey(canonicalProjectKey))
                .IsTrue();
        });
    }

    private static async Task RunInWorkspaceAsync(Func<string, Task> action)
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "lunapack-policy-security-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(workspace);
        try
        {
            await action(workspace);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string CreatePrivateSettingsDirectory(string workspace)
    {
        var directory = Directory
            .CreateDirectory(Path.Combine(workspace, UserSettingsStore.DirectoryName))
            .FullName;
        UserSettingsPathSecurity.Apply(directory, directory: true);
        return directory;
    }

    private static T RequireValue<T>(T? value)
        where T : class => value ?? throw new InvalidOperationException("Expected a value.");
}
