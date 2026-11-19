using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lunapack.Cli.Application.Serialization;

namespace Lunapack.Cli.Application.Diagnostics;

internal static class CliJsonWriter
{
    private const int SchemaVersion = 1;

    public static void WriteAudit(
        TextWriter output,
        CliJsonAuditResult? result,
        IReadOnlyList<CliJsonFinding> findings,
        CliJsonSummary summary
    ) =>
        Write(
            output,
            new CliJsonEnvelope<CliJsonAuditResult>(
                SchemaVersion,
                "audit",
                result,
                findings,
                summary
            ),
            LunapackJsonContext.Default.CliJsonAuditEnvelope
        );

    public static void WriteAuditFailure(TextWriter output) =>
        WriteAudit(output, null, [], CliJsonSummary.FromFindings([]));

    public static void WriteOutdated(TextWriter output, CliJsonOutdatedResult? result) =>
        Write(
            output,
            new CliJsonEnvelope<CliJsonOutdatedResult>(SchemaVersion, "outdated", result),
            LunapackJsonContext.Default.CliJsonOutdatedEnvelope
        );

    public static void WriteValidation(TextWriter output, CliJsonValidationResult? result) =>
        Write(
            output,
            new CliJsonEnvelope<CliJsonValidationResult>(SchemaVersion, "validate", result),
            LunapackJsonContext.Default.CliJsonValidationEnvelope
        );

    public static void WriteFailure(TextWriter output, string command)
    {
        switch (command)
        {
            case "audit":
                WriteAuditFailure(output);
                break;
            case "outdated":
                WriteOutdated(output, null);
                break;
            case "validate":
                WriteValidation(output, null);
                break;
            default:
                throw new ArgumentException(
                    $"Unsupported JSON command '{command}'.",
                    nameof(command)
                );
        }
    }

    private static void Write<T>(
        TextWriter output,
        CliJsonEnvelope<T> envelope,
        JsonTypeInfo<CliJsonEnvelope<T>> typeInfo
    )
        where T : class
    {
        output.WriteLine(JsonSerializer.Serialize(envelope, typeInfo));
    }
}
