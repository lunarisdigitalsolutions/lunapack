using Lunapack.Cli.Application.Diagnostics;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Application.Diagnostics;

public sealed class CliRedirectedConsoleTests
{
    [Test]
    public async Task Scenario_RedirectedConsole_PreservesInputAndInteractivity()
    {
        var inputConsole = new SpectreTestConsole();
        var outputConsole = new SpectreTestConsole();
        inputConsole.Profile.Capabilities.Interactive = true;
        outputConsole.Profile.Capabilities.Interactive = false;

        var redirected = new CliRedirectedConsole(inputConsole, outputConsole);

        await Assert.That(redirected.Profile.Capabilities.Interactive).IsTrue();
        await Assert.That(ReferenceEquals(redirected.Input, inputConsole.Input)).IsTrue();
    }
}
