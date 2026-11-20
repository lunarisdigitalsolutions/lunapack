using Lunapack.Cli.Packs.Lifecycle;

namespace Lunapack.Cli.UnitTests.Packs.Lifecycle;

public sealed class LifecycleHookExecutionResultTests
{
    [Test]
    public Task Scenario_TimeoutReceivesManifestFailure_PreservesTimeoutOutcome() =>
        AssertOriginalOutcomeIsPreservedAsync(LifecycleHookOutcome.TimedOut);

    [Test]
    public Task Scenario_CancellationReceivesManifestFailure_PreservesCancellationOutcome() =>
        AssertOriginalOutcomeIsPreservedAsync(LifecycleHookOutcome.Cancelled);

    private static async Task AssertOriginalOutcomeIsPreservedAsync(LifecycleHookOutcome outcome)
    {
        var result = new LifecycleHookExecutionResult(outcome, "original outcome");

        var combined = result.WithAdditionalError("manifest changed");

        await Assert.That(combined.Outcome).IsEqualTo(outcome);
        await Assert.That(combined.Error).Contains("original outcome");
        await Assert.That(combined.Error).Contains("manifest changed");
    }
}
