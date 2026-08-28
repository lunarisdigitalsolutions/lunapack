using System.IO.Abstractions.TestingHelpers;

namespace Lunapack.Cli.UnitTests;

public sealed class PackInstallationRequestTests
{
    [Test]
    public async Task Create_WhenScriptModeOmitted_UsesPrompt()
    {
        var result = PackInstallationRequest.Create(
            new MockFileSystem(),
            "C:\\project",
            "license-mit",
            null,
            false,
            [],
            false,
            []
        );

        await Assert.That(result.RequireValue().ScriptMode).IsEqualTo(ScriptExecutionMode.Prompt);
    }

    [Test]
    public async Task Create_WhenScriptModeIsSkip_PreservesMode()
    {
        var result = PackInstallationRequest.Create(
            new MockFileSystem(),
            "C:\\project",
            "license-mit",
            null,
            false,
            [],
            false,
            [],
            scriptMode: ScriptExecutionMode.Skip
        );

        await Assert.That(result.RequireValue().ScriptMode).IsEqualTo(ScriptExecutionMode.Skip);
    }

    [Test]
    public async Task Parse_WhenScriptModeUnsupported_ReturnsFailure()
    {
        var result = ScriptExecutionMode.Parse("always");

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Create_WhenParameterValueContainsEquals_PreservesValueRemainder()
    {
        var result = PackInstallationRequest.Create(
            new MockFileSystem(),
            "C:\\project",
            "license-mit",
            null,
            false,
            ["companyName=Lunaris=Digital"],
            false,
            []
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Parameters["companyName"])
            .IsEqualTo("Lunaris=Digital");
    }

    [Test]
    public async Task Create_WhenParameterNameRepeated_PreservesValuesInOrder()
    {
        var result = PackInstallationRequest.Create(
            new MockFileSystem(),
            "C:\\project",
            "license-mit",
            null,
            false,
            ["companyName=Lunaris", "companyName=Digital"],
            false,
            []
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().ParameterValues["companyName"])
            .IsEquivalentTo(["Lunaris", "Digital"]);
    }

    [Test]
    public async Task Create_WhenNoVariablesAndSkippedVariableProvided_ReturnsFailure()
    {
        var result = PackInstallationRequest.Create(
            new MockFileSystem(),
            "C:\\project",
            "license-mit",
            null,
            false,
            [],
            true,
            ["companyName"]
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Create_WhenDestinationAndManagedTargetRemappingProvided_ReturnsFailure()
    {
        var result = PackInstallationRequest.Create(
            new MockFileSystem(),
            "C:\\project",
            "madr-adr-template",
            "docs",
            false,
            [],
            false,
            [],
            ["docs/adr=docs/architecture"]
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("--destination cannot be combined");
    }

    [Test]
    public async Task Create_WhenDestinationUsesWindowsSeparators_NormalizesDestination()
    {
        var result = PackInstallationRequest.Create(
            new MockFileSystem(),
            "C:\\project",
            "madr-adr-template",
            "docs\\architecture",
            false,
            [],
            false,
            []
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Destination).IsEqualTo("docs/architecture");
    }

    [Test]
    public async Task Create_WhenDestinationUsesWindowsRootedPath_ReturnsFailure()
    {
        var result = PackInstallationRequest.Create(
            new MockFileSystem(),
            "C:\\project",
            "madr-adr-template",
            "C:\\",
            false,
            [],
            false,
            []
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Create_WhenManagedTargetRemappingSourceRepeated_ReturnsFailure()
    {
        var result = PackInstallationRequest.Create(
            new MockFileSystem(),
            "C:\\project",
            "madr-adr-template",
            null,
            false,
            [],
            false,
            [],
            ["docs/adr=docs/architecture", "docs/adr=docs/decisions"]
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }
}
