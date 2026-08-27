using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests;

public sealed class InstructionPresenterTests
{
    [Test]
    public async Task Present_WhenInteractive_ShowsIntroductionAndWaitsAfterEachStep()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        ansiConsole.Input.PushKey(ConsoleKey.Enter);
        ansiConsole.Input.PushKey(ConsoleKey.Enter);
        var presenter = new InstructionPresenter(new CliConsole(ansiConsole, CliLogLevel.Info));
        var instruction = CreateInstruction(
            "Read first.\n",
            [
                new InstructionStep(1, null, "Configure", "First body.\n"),
                new InstructionStep(1, 1, "Verify", "Second body."),
            ]
        );

        var result = presenter.Present(instruction);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(ansiConsole.Output).Contains("Read first.");
        await Assert.That(ansiConsole.Output).Contains("Step 1: Configure");
        await Assert.That(ansiConsole.Output).Contains("Step 1.1: Verify");
        await Assert
            .That(CountOccurrences(ansiConsole.Output, "Press Enter to continue..."))
            .IsEqualTo(2);
    }

    [Test]
    public async Task Present_WhenNonInteractive_ShowsAllStepsWithoutReadingOrPrompting()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = false;
        var presenter = new InstructionPresenter(new CliConsole(ansiConsole, CliLogLevel.Info));
        var instruction = CreateInstruction(
            string.Empty,
            [
                new InstructionStep(1, null, "First", "One"),
                new InstructionStep(2, null, "Second", "Two"),
            ]
        );

        var result = presenter.Present(instruction);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(ansiConsole.Output).Contains("Step 1: First");
        await Assert.That(ansiConsole.Output).Contains("Step 2: Second");
        await Assert.That(ansiConsole.Output).DoesNotContain("Press Enter to continue...");
    }

    [Test]
    public async Task Present_WhenContentContainsMarkdown_PreservesTextWithoutCompletionClaims()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = false;
        var presenter = new InstructionPresenter(new CliConsole(ansiConsole, CliLogLevel.Info));
        var content = "[Documentation](https://example.test)\n```sh\necho setup\n```";

        var result = presenter.Present(
            CreateInstruction(string.Empty, [new InstructionStep(1, null, null, content)])
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(ansiConsole.Output).Contains(content);
        await Assert.That(ansiConsole.Output).DoesNotContain("complete");
    }

    private static PreparedInstruction CreateInstruction(
        string introduction,
        IReadOnlyList<InstructionStep> steps
    ) =>
        new(
            new PackedHookFile("instructions/setup.md", "C:\\snapshot\\setup.md", "HASH"),
            false,
            new InstructionDocument(introduction, steps)
        );

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0; )
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}
