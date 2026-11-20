using System.Text.Json.Serialization;

namespace Lunapack.Cli.Application.Diagnostics;

internal sealed record CliJsonEnvelope<T>(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("result")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        T? Result,
    [property: JsonPropertyName("findings")] IReadOnlyList<CliJsonFinding>? Findings = null,
    [property: JsonPropertyName("summary")] CliJsonSummary? Summary = null
)
    where T : class;
