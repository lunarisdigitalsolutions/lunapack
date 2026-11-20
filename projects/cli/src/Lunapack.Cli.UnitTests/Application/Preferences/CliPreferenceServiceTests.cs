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
        await Assert
            .That(preferences.ResourceLimits)
            .IsEqualTo(
                new ResourceLimits(
                    1_048_576,
                    1_048_576,
                    67_108_864,
                    16_777_216,
                    2_147_483_648,
                    67_108_864,
                    536_870_912,
                    64,
                    1_024,
                    100_000,
                    10_000,
                    100_000,
                    100_000,
                    536_870_912,
                    536_870_912,
                    2_147_483_648
                )
            );
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
    public async Task Resolve_WhenResourceLimitExistsAtMultipleScopes_UsesMostSpecificFiniteValue()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var service = CreatePreferenceService(workspace);
        const string key = "resource-limits.pack-manifest-bytes";

        var global = await service.SetAsync(
            workspace.Path,
            key,
            "2097152",
            CliPreferenceService.Scope.Global
        );
        var userProject = await service.SetAsync(
            workspace.Path,
            key,
            "4194304",
            CliPreferenceService.Scope.UserProject
        );
        var project = await service.SetAsync(
            workspace.Path,
            key,
            "524288",
            CliPreferenceService.Scope.Local
        );
        var resolved = await service.ResolveAsync(workspace.Path);
        var projectRemoved = await service.RemoveAsync(
            workspace.Path,
            key,
            CliPreferenceService.Scope.Local
        );
        var userProjectResolved = await service.ResolveAsync(workspace.Path);

        await Assert.That(global.IsSuccess).IsTrue();
        await Assert.That(userProject.IsSuccess).IsTrue();
        await Assert.That(project.IsSuccess).IsTrue();
        await Assert
            .That(resolved.RequireValue().ResourceLimits.PackManifestBytes)
            .IsEqualTo(524288);
        await Assert.That(projectRemoved.IsSuccess).IsTrue();
        await Assert
            .That(userProjectResolved.RequireValue().ResourceLimits.PackManifestBytes)
            .IsEqualTo(4194304);
    }

    [Test]
    public async Task Resolve_WhenProjectLimitIsHigherThanUserLimit_KeepsStricterUserValue()
    {
        using var workspace = new TestWorkspace();
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var service = CreatePreferenceService(workspace);
        const string key = "resource-limits.pack-manifest-bytes";
        await service.SetAsync(
            workspace.Path,
            key,
            "262144",
            CliPreferenceService.Scope.UserProject
        );
        await service.SetAsync(workspace.Path, key, "1048576", CliPreferenceService.Scope.Local);

        var resolved = await service.ResolveAsync(workspace.Path);

        await Assert
            .That(resolved.RequireValue().ResourceLimits.PackManifestBytes)
            .IsEqualTo(262144);
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

    [Test]
    [Arguments("0")]
    [Arguments("-1")]
    [Arguments("1.5")]
    [Arguments("67108865")]
    public async Task Set_WhenResourceLimitIsInvalid_DoesNotWriteSettings(string value)
    {
        using var workspace = new TestWorkspace();
        var service = CreatePreferenceService(workspace);

        var result = await service.SetAsync(
            workspace.Path,
            "resource-limits.pack-manifest-bytes",
            value,
            CliPreferenceService.Scope.Global
        );

        await Assert.That(result.IsSuccess).IsFalse();
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
