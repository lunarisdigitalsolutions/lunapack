using System.Text.Json.Serialization;

namespace Lunapack.Cli.Application.Diagnostics;

internal sealed record CliJsonFinding(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("subject")] CliJsonFinding.SubjectInfo Subject,
    [property: JsonPropertyName("message")] string Message
)
{
    internal sealed record SubjectInfo(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("packId")] string? PackId = null,
        [property: JsonPropertyName("path")] string? Path = null
    );
}
