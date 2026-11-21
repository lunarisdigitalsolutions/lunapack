using Lunapack.Cli.Packs.Lifecycle;

namespace Lunapack.Cli.UnitTests.Packs.Lifecycle;

public sealed class LifecycleHookContainmentTests
{
    [Test]
    public async Task ParseMacProcessRow_WhenPidPpidPgidColumnsOnly_ParsesRow()
    {
        var row = LifecycleHookContainment.ParseMacProcessRow("123 456 789");

        await Assert.That(row).IsNotNull();
        if (row is not { } parsed)
        {
            throw new InvalidOperationException("Expected a macOS process row.");
        }

        await Assert.That(parsed.ProcessId).IsEqualTo(123);
        await Assert.That(parsed.ParentProcessId).IsEqualTo(456);
        await Assert.That(parsed.ProcessGroupId).IsEqualTo(789);
    }

    [Test]
    public async Task ParseMacProcessRow_WhenOutputIsMalformed_ReturnsNull()
    {
        var row = LifecycleHookContainment.ParseMacProcessRow("ps: sid: keyword not found");

        await Assert.That(row).IsNull();
    }
}
