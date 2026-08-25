using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests;

public sealed class CliConsoleTests
{
    [Test]
    public async Task Prompt_WhenStringParameterRequired_UsesDisplayMetadataAndTypedInput()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Input.PushTextWithEnter("Prompted Corporation");
        var console = new CliConsole(ansiConsole, CliLogLevel.Info);

        var value = console.Prompt(
            new PackParameterPrompt(
                "companyName",
                new PackParameterDefinition(
                    PackParameterType.String,
                    true,
                    [],
                    "Company name",
                    "Legal entity name."
                )
            )
        );

        await Assert.That(value).IsEqualTo("Prompted Corporation");
        await Assert.That(ansiConsole.Output).Contains("Company name");
        await Assert.That(ansiConsole.Output).Contains("Legal entity name.");
    }
}
