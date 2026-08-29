using Lunapack.Cli.Application.Guidance;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Application.Guidance;

public sealed class NextStepRendererTests
{
    [Test]
    public async Task Render_WhenOneActionProvided_UsesSingularHeadingAndEscapesMarkup()
    {
        var ansiConsole = new SpectreTestConsole();
        var renderer = new NextStepRenderer(new CliConsole(ansiConsole, CliLogLevel.Info));

        renderer.Render([new NextStepRecommendation("[Inspect]", "luna inspect <pack>")]);

        await Assert.That(ansiConsole.Output).Contains("Next step:");
        await Assert.That(ansiConsole.Output).Contains("[Inspect]");
        await Assert.That(ansiConsole.Output).Contains("luna inspect <pack>");
    }

    [Test]
    public async Task Render_WhenSeveralActionsProvided_NumbersActionsInOrder()
    {
        var ansiConsole = new SpectreTestConsole();
        var renderer = new NextStepRenderer(new CliConsole(ansiConsole, CliLogLevel.Info));

        renderer.Render([
            new NextStepRecommendation("Discover", "luna discover"),
            new NextStepRecommendation("Install", "luna install <pack>"),
        ]);

        await Assert.That(ansiConsole.Output).Contains("Next steps:");
        await Assert.That(ansiConsole.Output).Contains("1. Discover");
        await Assert.That(ansiConsole.Output).Contains("2. Install");
    }

    [Test]
    public async Task Render_WhenNoActionsProvided_ProducesNoOutput()
    {
        var ansiConsole = new SpectreTestConsole();
        var renderer = new NextStepRenderer(new CliConsole(ansiConsole, CliLogLevel.Info));

        renderer.Render([]);

        await Assert.That(ansiConsole.Output).IsEmpty();
    }
}
