using Lunapack.Cli.Packs;
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

    [Test]
    public async Task Prompt_WhenStringParameterHasDefault_AcceptsDefaultOnEnter()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Input.PushTextWithEnter(string.Empty);
        var console = new CliConsole(ansiConsole, CliLogLevel.Info);

        var value = console.Prompt(
            new PackParameterPrompt(
                "companyName",
                new PackParameterDefinition(PackParameterType.String, true, [], Default: "Lunaris")
            )
        );

        await Assert.That(value).IsEqualTo("Lunaris");
    }

    [Test]
    public async Task PromptValues_WhenMultiSelectEnumRequired_ReturnsSelectedValues()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        ansiConsole.Input.PushKey(ConsoleKey.Spacebar);
        ansiConsole.Input.PushKey(ConsoleKey.DownArrow);
        ansiConsole.Input.PushKey(ConsoleKey.Spacebar);
        ansiConsole.Input.PushKey(ConsoleKey.Enter);
        var console = new CliConsole(ansiConsole, CliLogLevel.Info);

        var values = console.PromptValues(
            new PackParameterPrompt(
                "features",
                new PackParameterDefinition(
                    PackParameterType.Enum,
                    true,
                    ["api", "docker"],
                    Multiple: true
                )
            )
        );

        await Assert.That(values).IsEquivalentTo(["api", "docker"]);
    }

    [Test]
    public async Task SemanticInfo_WhenMinimumLevelIsWarning_IsSuppressed()
    {
        var ansiConsole = new SpectreTestConsole();
        var console = new CliConsole(ansiConsole, CliLogLevel.Warning);

        console.Success("Installed");
        console.Accent("Next steps");

        await Assert.That(ansiConsole.Output).IsEmpty();
    }
}
