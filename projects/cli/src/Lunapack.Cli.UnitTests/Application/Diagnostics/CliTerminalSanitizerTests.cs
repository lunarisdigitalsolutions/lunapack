using Lunapack.Cli.Application.Diagnostics;

namespace Lunapack.Cli.UnitTests.Application.Diagnostics;

public sealed class CliTerminalSanitizerTests
{
    [Test]
    public async Task Scenario_DiagnosticsWithControls_EscapeControlsWithoutChangingText()
    {
        const string value = "safe\u001b[2J\u0007\r\nforged\t\u0085\u2028\u202e";

        var sanitized = CliTerminalSanitizer.Sanitize(value);

        await Assert.That(sanitized).DoesNotContain("\u001b");
        await Assert.That(sanitized).DoesNotContain("\u0007");
        await Assert.That(sanitized).DoesNotContain("\r");
        await Assert.That(sanitized).DoesNotContain("\n");
        await Assert.That(sanitized).DoesNotContain("\t");
        await Assert.That(sanitized).Contains("safe\\u001b[2J\\u0007");
        await Assert.That(sanitized).Contains("\\u000d\\u000a");
        await Assert.That(sanitized).Contains("\\u0085\\u2028\\u202e");
    }

    [Test]
    public async Task Scenario_WriterChunk_PreservesOnlyTrailingLineEnding()
    {
        const string value = "first\nforged\u001b[2J\n";

        var sanitized = CliTerminalSanitizer.SanitizeWriterChunk(value);

        await Assert.That(sanitized).IsEqualTo("first\\u000aforged\\u001b[2J\n");
    }
}
