using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.UnitTests.Packs.Lifecycle;

public sealed class LifecycleHookPolicyResolverTests
{
    [Test]
    public async Task Scenario_PoliciesAcrossScopes_UnionNamesAndUseShortestTimeout()
    {
        var result = LifecycleHookPolicyResolver.ResolveForUnix(
            new LifecycleHookPolicy
            {
                AllowedEnvironmentVariables = ["PROJECT_VALUE"],
                TimeoutSeconds = 120,
            },
            new LifecycleHookPolicy
            {
                AllowedEnvironmentVariables = ["LOCAL_VALUE"],
                TimeoutSeconds = 45,
            },
            new LifecycleHookPolicy
            {
                AllowedEnvironmentVariables = ["GLOBAL_VALUE"],
                TimeoutSeconds = 90,
            }
        );

        await Assert.That(result.AllowedEnvironmentVariables).Contains("PROJECT_VALUE");
        await Assert.That(result.AllowedEnvironmentVariables).Contains("LOCAL_VALUE");
        await Assert.That(result.AllowedEnvironmentVariables).Contains("GLOBAL_VALUE");
        await Assert.That(result.TimeoutSeconds).IsEqualTo(45);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Scenario_NoTimeoutConfigured_UsesDefaultAndPlatformBaseline(bool isWindows)
    {
        var result = isWindows
            ? LifecycleHookPolicyResolver.ResolveForWindows(null, null, null)
            : LifecycleHookPolicyResolver.ResolveForUnix(null, null, null);

        await Assert.That(result.TimeoutSeconds).IsEqualTo(300);
        await Assert.That(result.AllowedEnvironmentVariables).Contains("PATH");
        await Assert.That(result.AllowedEnvironmentVariables).Contains("TEMP");
        await Assert.That(result.AllowedEnvironmentVariables).Contains("TMP");
        await Assert.That(result.AllowedEnvironmentVariables).Contains("TMPDIR");
        await Assert
            .That(result.AllowedEnvironmentVariables)
            .Contains(isWindows ? "SYSTEMROOT" : "HOME");
    }

    [Test]
    [Arguments(301)]
    [Arguments(1800)]
    public async Task Scenario_TimeoutAboveDefault_UsesConfiguredValue(int timeoutSeconds)
    {
        var result = LifecycleHookPolicyResolver.ResolveForUnix(
            new LifecycleHookPolicy { TimeoutSeconds = timeoutSeconds },
            null,
            null
        );

        await Assert.That(result.TimeoutSeconds).IsEqualTo(timeoutSeconds);
    }

    [Test]
    public async Task Scenario_WindowsVariableNames_CompareCaseInsensitively()
    {
        var result = LifecycleHookPolicyResolver.ResolveForWindows(
            new LifecycleHookPolicy { AllowedEnvironmentVariables = ["CustomValue"] },
            null,
            null
        );

        await Assert.That(result.AllowedEnvironmentVariables).Contains("customvalue");
    }

    [Test]
    public async Task Scenario_UnixVariableNames_CompareOrdinally()
    {
        var result = LifecycleHookPolicyResolver.ResolveForUnix(
            new LifecycleHookPolicy { AllowedEnvironmentVariables = ["CustomValue"] },
            null,
            null
        );

        await Assert.That(result.AllowedEnvironmentVariables).Contains("CustomValue");
        await Assert.That(result.AllowedEnvironmentVariables).DoesNotContain("CUSTOMVALUE");
    }
}
