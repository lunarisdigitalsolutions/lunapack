using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Packs.ExternalSources;

public sealed class ExternalSourcePromptTests
{
    [Test]
    [Arguments("Y", true)]
    [Arguments("n", false)]
    public async Task ApproveAsync_WhenUserResponds_ReturnsDecisionAndRendersSourceDetails(
        string response,
        bool expected
    )
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        ansiConsole.Input.PushTextWithEnter(response);
        var approver = new ConsoleExternalSourceApprover(
            new CliConsole(ansiConsole, CliLogLevel.Info)
        );

        var approved = await approver.ApproveAsync([CreateRequirement()], CancellationToken.None);

        await Assert.That(approved).IsEqualTo(expected);
        await Assert.That(ansiConsole.Output).Contains("engineering");
        await Assert.That(ansiConsole.Output).Contains("required by foundation:shared");
        await Assert.That(ansiConsole.Output).Contains("2 file selector(s)");
    }

    [Test]
    public async Task PromptAsync_WhenIdentifierProvided_ReturnsTrimmedValueAndWarnsAboutConflict()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        ansiConsole.Input.PushTextWithEnter(" replacement ");
        var prompter = new ConsoleExternalSourceIdentifierPrompter(
            new CliConsole(ansiConsole, CliLogLevel.Info)
        );

        var identifier = await prompter.PromptAsync(
            CreateRequirement(),
            "engineering",
            CancellationToken.None
        );

        await Assert.That(identifier).IsEqualTo("replacement");
        await Assert.That(ansiConsole.Output).Contains("already used for another source");
    }

    [Test]
    public async Task PromptAsync_WhenIdentifierIsEmpty_ReturnsNull()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        ansiConsole.Input.PushKey(ConsoleKey.Enter);
        var prompter = new ConsoleExternalSourceIdentifierPrompter(
            new CliConsole(ansiConsole, CliLogLevel.Info)
        );

        var identifier = await prompter.PromptAsync(
            CreateRequirement(),
            "engineering",
            CancellationToken.None
        );

        await Assert.That(identifier).IsNull();
    }

    private static ExternalSourceRequirementGroup CreateRequirement() =>
        new(
            new SourceFingerprint
            {
                Type = SourceFingerprint.GitType,
                Identity = "example.test/engineering/packs",
                Ref = "refs/heads/main",
                Path = "/packs",
            },
            new ProjectConfiguration.GitSource
            {
                Name = "engineering",
                Url = "https://example.test/engineering/packs.git",
                Ref = "refs/heads/main",
                Path = "packs",
            },
            [new ExternalSourceRequirementUse("foundation", "1.0.0", "shared", "Shared files", 2)],
            "engineering",
            IsExisting: false,
            IdentifierConflict: null
        );
}
