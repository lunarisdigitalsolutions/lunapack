using Lunapack.Cli.Doctor;

namespace Lunapack.Cli.UnitTests.Doctor;

public sealed class DoctorTextTests
{
    [Test]
    public async Task Sanitize_WhenValueContainsTerminalControls_UsesVisibleEscapes()
    {
        var input = "safe\r\n\u001b]0;secret\u0007\u202E";

        var result = DoctorText.Sanitize(input);

        await Assert.That(result).DoesNotContain("\r");
        await Assert.That(result).DoesNotContain("\n");
        await Assert.That(result).DoesNotContain("\u001b");
        await Assert.That(result).DoesNotContain("\u202E");
        await Assert.That(result).Contains(@"\u001B");
        await Assert.That(result).Contains(@"\u202E");
    }
}
