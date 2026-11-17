using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.UnitTests.Application.Preferences;

public sealed class CliPreferenceServiceTests
{
    [Test]
    public async Task Resolve_WhenNoPreferenceIsConfigured_UsesBuiltInDefaults()
    {
        using var workspace = new TestWorkspace();
        var service = CreatePreferenceService(workspace);

        var result = await service.ResolveAsync(workspace.Path);

        await Assert.That(result.IsSuccess).IsTrue();
        var preferences = result.RequireValue();
        await Assert.That(preferences.DiscoverOnlyComposites).IsFalse();
        await Assert.That(preferences.SearchOnlyComposites).IsFalse();
        await Assert.That(preferences.NextStepsEnabled).IsTrue();
    }

    [Test]
    public async Task Resolve_WhenValuesExistAtMultipleScopes_UsesMostSpecificIncludingFalse()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var service = CreatePreferenceService(workspace);

        var global = await service.SetAsync(
            workspace.Path,
            "next-steps.enabled",
            "false",
            CliPreferenceService.Scope.Global
        );
        var userProject = await service.SetAsync(
            workspace.Path,
            "next-steps.enabled",
            "true",
            CliPreferenceService.Scope.UserProject
        );
        var project = await service.ResolveAsync(workspace.Path);
        var local = await service.SetAsync(
            workspace.Path,
            "next-steps.enabled",
            "false",
            CliPreferenceService.Scope.Local
        );
        var localResolved = await service.ResolveAsync(workspace.Path);
        var localRemoved = await service.RemoveAsync(
            workspace.Path,
            "next-steps.enabled",
            CliPreferenceService.Scope.Local
        );
        var userProjectResolved = await service.ResolveAsync(workspace.Path);
        var userProjectRemoved = await service.RemoveAsync(
            workspace.Path,
            "next-steps.enabled",
            CliPreferenceService.Scope.UserProject
        );
        var globalResolved = await service.ResolveAsync(workspace.Path);

        await Assert.That(global.IsSuccess).IsTrue();
        await Assert.That(userProject.IsSuccess).IsTrue();
        await Assert.That(project.RequireValue().NextStepsEnabled).IsTrue();
        await Assert.That(local.IsSuccess).IsTrue();
        await Assert.That(localResolved.RequireValue().NextStepsEnabled).IsFalse();
        await Assert.That(localRemoved.IsSuccess).IsTrue();
        await Assert.That(userProjectResolved.RequireValue().NextStepsEnabled).IsTrue();
        await Assert.That(userProjectRemoved.IsSuccess).IsTrue();
        await Assert.That(globalResolved.RequireValue().NextStepsEnabled).IsFalse();
    }

    [Test]
    public async Task Set_WhenKeyOrValueIsInvalid_DoesNotWriteSettings()
    {
        using var workspace = new TestWorkspace();
        var service = CreatePreferenceService(workspace);

        var unknownKey = await service.SetAsync(
            workspace.Path,
            "discover.unknown",
            "true",
            CliPreferenceService.Scope.Global
        );
        var invalidValue = await service.SetAsync(
            workspace.Path,
            "discover.only-composites",
            "yes",
            CliPreferenceService.Scope.Global
        );

        await Assert.That(unknownKey.IsSuccess).IsFalse();
        await Assert.That(invalidValue.IsSuccess).IsFalse();
        await Assert.That(File.Exists(GetSettingsPath(workspace))).IsFalse();
    }

    private static CliPreferenceService CreatePreferenceService(TestWorkspace workspace)
    {
        var userSettingsStore = new UserSettingsStore(
            workspace.FileSystem,
            Path.Combine(workspace.Path, "profile")
        );
        return new CliPreferenceService(
            workspace.FileSystem,
            workspace.StateStore,
            userSettingsStore
        );
    }

    private static string GetSettingsPath(TestWorkspace workspace) =>
        Path.Combine(
            workspace.Path,
            "profile",
            UserSettingsStore.DirectoryName,
            UserSettingsStore.FileName
        );
}
