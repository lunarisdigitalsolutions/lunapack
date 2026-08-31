using Lunapack.Cli.Application;

namespace Lunapack.Cli.UnitTests.Application;

public sealed class CliLogLevelTests
{
    [Test]
    public async Task TryParse_WhenSupportedLevelProvided_MapsToCliLogLevel()
    {
        var levels = new (string Value, CliLogLevel Expected)[]
        {
            ("verbose", CliLogLevel.Verbose),
            ("debug", CliLogLevel.Debug),
            ("info", CliLogLevel.Info),
            ("warning", CliLogLevel.Warning),
            ("error", CliLogLevel.Error),
        };

        foreach (var (value, expected) in levels)
        {
            var parsed = CliLogLevelParser.TryParse(
                ["discover", "--log-level", value],
                out var level,
                out var error
            );

            await Assert.That(parsed).IsTrue();
            await Assert.That(error).IsNull();
            await Assert.That(level).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task TryParse_WhenLogLevelValueMissing_ReturnsFailure()
    {
        var parsed = CliLogLevelParser.TryParse(["discover", "--log-level"], out _, out var error);

        await Assert.That(parsed).IsFalse();
        await Assert.That(error).Contains("Log level");
    }

    [Test]
    public async Task TryParse_WhenLogLevelValueIsNotLowerCase_ReturnsFailure()
    {
        var parsed = CliLogLevelParser.TryParse(
            ["discover", "--log-level", "Debug"],
            out _,
            out var error
        );

        await Assert.That(parsed).IsFalse();
        await Assert.That(error).Contains("Log level");
    }

    [Test]
    public async Task TryParse_WhenShortLogLevelAliasProvided_MapsToCliLogLevel()
    {
        var parsed = CliLogLevelParser.TryParse(
            ["discover", "-ll=debug"],
            out var level,
            out var error
        );

        await Assert.That(parsed).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(level).IsEqualTo(CliLogLevel.Debug);
    }
}
