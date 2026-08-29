using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.UnitTests;

public sealed class SourceOutputFormatterTests
{
    [Test]
    public async Task Scenario_LocalSource_FormatsTypeAndPath()
    {
        var source = new ProjectConfiguration.LocalSource
        {
            Name = "platform",
            Path = "packs/platform",
        };

        var output = SourceOutputFormatter.Format(source);

        await Assert
            .That(output)
            .IsEqualTo(
                "platform - local - path: packs/platform - identity: local(path=packs/platform)"
            );
    }

    [Test]
    public async Task Scenario_GitSourceHasProperties_FormatsTypeAndConfiguredProperties()
    {
        var source = new ProjectConfiguration.GitSource
        {
            Name = "shared",
            Url = "https://example.test/platform-packs.git",
            Ref = "main",
            Path = "packs/platform",
            TimeoutSeconds = 120,
        };

        var output = SourceOutputFormatter.Format(source);

        await Assert
            .That(output)
            .IsEqualTo(
                "shared - git - url: https://example.test/platform-packs.git - ref: main - path: packs/platform - timeoutSeconds: 120 - identity: git(url=https://example.test/platform-packs.git, ref=main, path=packs/platform)"
            );
    }
}
