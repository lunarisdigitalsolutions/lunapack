using System.Text.Json;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Application.Diagnostics;

public sealed class CliApplicationJsonOutputTests
{
    [Test]
    public async Task Scenario_AuditJson_PreservesEmptyArraysWithoutTextOutput()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var existingOutput = ansiConsole.Output;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["audit", "--format", "json"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsZero();
        await Assert.That(ansiConsole.Output).IsEqualTo(existingOutput);
        using var document = JsonDocument.Parse(output.ToString());
        var result = document.RootElement.GetProperty("result");
        await Assert.That(result.GetProperty("packs").GetArrayLength()).IsZero();
        await Assert.That(result.GetProperty("links").GetArrayLength()).IsZero();
        await Assert.That(document.RootElement.GetProperty("findings").GetArrayLength()).IsZero();
        await Assert
            .That(
                document.RootElement.GetProperty("summary").GetProperty("totalFindings").GetInt32()
            )
            .IsZero();
    }

    [Test]
    public async Task Scenario_StrictAuditJson_ReturnsUsageFailureEnvelope()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var existingOutput = ansiConsole.Output;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["audit", "--strict", "--format", "json"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(error.ToString()).Contains("cannot be used with '--format json'");
        using var document = JsonDocument.Parse(output.ToString());
        await Assert
            .That(document.RootElement.GetProperty("result").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
        await Assert.That(ansiConsole.Output).IsEqualTo(existingOutput);
    }

    [Test]
    public async Task Scenario_AuditUnsupportedFormat_WritesOnlyErrorOutput()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var existingOutput = ansiConsole.Output;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["audit", "--format", "yaml"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsNotZero();
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("Unsupported output format");
        await Assert.That(ansiConsole.Output).IsEqualTo(existingOutput);
    }

    [Test]
    public async Task Scenario_AuditMissingFormatValue_WritesOnlyParseErrors()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["audit", "--format"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsNotZero();
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("Unsupported output format");
        await Assert.That(ansiConsole.Output).IsEmpty();
    }

    [Test]
    public async Task Scenario_AuditJsonHelp_WritesHelpToStandardError()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["audit", "--format", "json", "--help"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsZero();
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("Usage:");
        await Assert.That(ansiConsole.Output).IsEmpty();
    }

    [Test]
    public async Task Scenario_AuditInvalidFormatHelp_WritesOnlyFormatError()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["audit", "--format", "yaml", "--help"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsNotZero();
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).Contains("Unsupported output format");
        await Assert.That(error.ToString()).DoesNotContain("Usage:");
        await Assert.That(ansiConsole.Output).IsEmpty();
    }

    [Test]
    public async Task Scenario_AuditTextHelp_RemainsOnStandardOutput()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["audit", "--format", "text", "--help"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsZero();
        await Assert.That(output.ToString()).Contains("Usage:");
        await Assert.That(error.ToString()).IsEmpty();
        await Assert.That(ansiConsole.Output).IsEmpty();
    }

    [Test]
    public async Task Scenario_MaliciousFormatValue_CannotInjectTerminalControls()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        using var output = new StringWriter();
        using var error = new StringWriter();
        const string malicious = "yaml\u001b]0;forged\u0007\nforged";

        var exitCode = await workspace.Application.RunAsync(
            ["audit", "--format", malicious],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsNotZero();
        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(error.ToString()).DoesNotContain("\u001b");
        await Assert.That(error.ToString()).DoesNotContain("\u0007");
        await Assert.That(error.ToString()).DoesNotContain("\nforged");
        await Assert.That(error.ToString()).Contains("\\u001b");
        await Assert.That(error.ToString()).Contains("\\u000a");
    }

    [Test]
    public async Task Scenario_MaliciousParserToken_CannotInjectTerminalControls()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        using var output = new StringWriter();
        using var error = new StringWriter();
        const string maliciousOption = "--unknown\u001b[2J\nforged";

        var exitCode = await workspace.Application.RunAsync(
            ["audit", "--format", "json", maliciousOption],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsNotZero();
        await Assert.That(error.ToString()).DoesNotContain("\u001b");
        await Assert.That(error.ToString()).DoesNotContain("\nforged");
        await Assert.That(error.ToString()).Contains("\\u001b");
        await Assert.That(error.ToString()).Contains("\\u000a");
        using var document = JsonDocument.Parse(output.ToString());
        await Assert
            .That(document.RootElement.GetProperty("result").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task Scenario_OutdatedJsonOffline_ReportsUncheckedRemoteState()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await workspace.Application.RunAsync(["init"], workspace.Path);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        var existingOutput = ansiConsole.Output;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["outdated", "--offline", "--format", "json"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsZero();
        await Assert.That(ansiConsole.Output).IsEqualTo(existingOutput);
        using var document = JsonDocument.Parse(output.ToString());
        var result = document.RootElement.GetProperty("result");
        await Assert.That(result.GetProperty("updates").GetArrayLength()).IsZero();
        await Assert.That(result.GetProperty("remoteRefsChecked").GetBoolean()).IsFalse();
        await Assert.That(error.ToString()).Contains("Remote refs were not checked");
    }

    [Test]
    public async Task Scenario_ValidateJsonValidPack_WritesTrue()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await AddLocalPackAsync(workspace, createSourceFile: true);
        var existingOutput = ansiConsole.Output;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["validate", "example", "--format", "json"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsZero();
        await Assert.That(ansiConsole.Output).IsEqualTo(existingOutput);
        await Assert.That(error.ToString()).IsEmpty();
        using var document = JsonDocument.Parse(output.ToString());
        await Assert
            .That(document.RootElement.GetProperty("result").GetProperty("valid").GetBoolean())
            .IsTrue();
    }

    [Test]
    public async Task Scenario_ValidateJsonInvalidPack_WritesFalseAndIssuesToError()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await AddLocalPackAsync(workspace, createSourceFile: false);
        var existingOutput = ansiConsole.Output;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["validate", "example", "--format", "json"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).IsEqualTo(existingOutput);
        await Assert.That(error.ToString()).Contains("source file");
        await Assert.That(error.ToString()).Contains("unavailable");
        using var document = JsonDocument.Parse(output.ToString());
        await Assert
            .That(document.RootElement.GetProperty("result").GetProperty("valid").GetBoolean())
            .IsFalse();
    }

    [Test]
    public async Task Scenario_ValidateJsonUnavailablePack_WritesNullResult()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var existingOutput = ansiConsole.Output;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["validate", "missing", "--format", "json"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).IsEqualTo(existingOutput);
        await Assert.That(error.ToString()).Contains("unavailable");
        using var document = JsonDocument.Parse(output.ToString());
        await Assert
            .That(document.RootElement.GetProperty("result").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task Scenario_JsonParseFailure_RoutesDiagnosticsToErrorAndKeepsStdoutJson()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["audit", "--format", "json", "--log-level", "debug", "--unknown-option"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsNotZero();
        await Assert.That(ansiConsole.Output).IsEmpty();
        await Assert.That(error.ToString()).Contains("Running CLI command");
        using var document = JsonDocument.Parse(output.ToString());
        await Assert
            .That(document.RootElement.GetProperty("command").GetString())
            .IsEqualTo("audit");
        await Assert
            .That(document.RootElement.GetProperty("result").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task Scenario_JsonModeWithInvalidLogLevel_WritesOneFailureEnvelope()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await workspace.Application.RunAsync(
            ["outdated", "--format", "json", "--log-level", "invalid"],
            workspace.Path,
            output,
            error
        );

        await Assert.That(exitCode).IsNotZero();
        await Assert.That(error.ToString()).Contains("Log level");
        await Assert.That(ansiConsole.Output).IsEmpty();
        using var document = JsonDocument.Parse(output.ToString());
        await Assert
            .That(document.RootElement.GetProperty("command").GetString())
            .IsEqualTo("outdated");
        await Assert
            .That(document.RootElement.GetProperty("result").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
    }

    private static async Task AddLocalPackAsync(TestWorkspace workspace, bool createSourceFile)
    {
        var packDirectory = Path.Combine(workspace.Path, "source", "example");
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templatesDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(packDirectory, "pack.yml"),
            "id: example\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: content.txt\n"
        );
        if (createSourceFile)
        {
            await File.WriteAllTextAsync(
                Path.Combine(templatesDirectory, "content.txt"),
                "content"
            );
        }

        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
    }
}
