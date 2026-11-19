using System.Text.Json;
using Lunapack.Cli.Application.Diagnostics;

namespace Lunapack.Cli.UnitTests.Application.Diagnostics;

public sealed class CliJsonWriterTests
{
    [Test]
    public async Task Scenario_AuditFailure_WritesCompleteVersionedEnvelope()
    {
        using var output = new StringWriter();

        CliJsonWriter.WriteAuditFailure(output);

        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("command").GetString()).IsEqualTo("audit");
        await Assert.That(root.GetProperty("result").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(root.GetProperty("findings").ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert
            .That(root.GetProperty("summary").GetProperty("totalFindings").GetInt32())
            .IsZero();
        await Assert
            .That(
                root.GetProperty("summary").GetProperty("bySeverity").GetProperty("info").GetInt32()
            )
            .IsZero();
    }

    [Test]
    public async Task Scenario_OutdatedNullFields_PreserveStableArraysAndNulls()
    {
        using var output = new StringWriter();
        var result = new CliJsonOutdatedResult(
            [],
            [],
            [
                new CliJsonOutdatedResult.IntegrityViolation(
                    "example",
                    "1.0.0",
                    null,
                    "locked",
                    null
                ),
            ],
            [],
            false
        );

        CliJsonWriter.WriteOutdated(output, result);

        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        var resultElement = root.GetProperty("result");
        await Assert.That(root.GetProperty("command").GetString()).IsEqualTo("outdated");
        await Assert.That(resultElement.GetProperty("updates").GetArrayLength()).IsZero();
        await Assert.That(resultElement.GetProperty("outdatedLinks").GetArrayLength()).IsZero();
        await Assert
            .That(
                resultElement
                    .GetProperty("integrityViolations")[0]
                    .GetProperty("sourceName")
                    .ValueKind
            )
            .IsEqualTo(JsonValueKind.Null);
        await Assert
            .That(
                resultElement
                    .GetProperty("integrityViolations")[0]
                    .GetProperty("observedManifestDigest")
                    .ValueKind
            )
            .IsEqualTo(JsonValueKind.Null);
        await Assert.That(resultElement.GetProperty("remoteRefsChecked").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task Scenario_ValidationResult_UsesCamelCasePropertyNames()
    {
        using var output = new StringWriter();

        CliJsonWriter.WriteValidation(output, new CliJsonValidationResult(true));

        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        await Assert.That(root.GetProperty("command").GetString()).IsEqualTo("validate");
        await Assert.That(root.GetProperty("result").GetProperty("valid").GetBoolean()).IsTrue();
        await Assert.That(root.TryGetProperty("schemaVersion", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("SchemaVersion", out _)).IsFalse();
    }
}
