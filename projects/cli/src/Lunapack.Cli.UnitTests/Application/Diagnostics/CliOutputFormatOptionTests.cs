using Lunapack.Cli.Application.Diagnostics;

namespace Lunapack.Cli.UnitTests.Application.Diagnostics;

public sealed class CliOutputFormatOptionTests
{
    [Test]
    public async Task Scenario_JsonFormatForSupportedCommand_ResolvesCommand()
    {
        var args = new[] { "--workspace", "workspace", "audit", "--format", "json" };

        var resolved = CliOutputFormatOption.TryGetJsonCommand(args, out var command);

        await Assert.That(resolved).IsTrue();
        await Assert.That(command).IsEqualTo("audit");
    }

    [Test]
    public async Task Scenario_InvalidFormat_RoutesDiagnosticsWithoutSelectingJsonCommand()
    {
        var args = new[] { "outdated", "--format", "yaml" };

        var resolved = CliOutputFormatOption.TryGetJsonCommand(args, out _);

        await Assert.That(resolved).IsFalse();
        await Assert.That(CliOutputFormatOption.RoutesDiagnosticsToError(args)).IsTrue();
    }

    [Test]
    public async Task Scenario_TextFormat_IsDefaultAndKeepsNormalOutputRouting()
    {
        var args = new[] { "validate", "example" };

        var valid = CliOutputFormatOption.TryParse(null, out var format);

        await Assert.That(valid).IsTrue();
        await Assert.That(format).IsEqualTo(CliOutputFormat.Text);
        await Assert.That(CliOutputFormatOption.RoutesDiagnosticsToError(args)).IsFalse();
    }
}
