using System.Text.Json.Serialization;

namespace Lunapack.Cli.Application.Diagnostics;

internal sealed record CliJsonValidationResult([property: JsonPropertyName("valid")] bool Valid);
