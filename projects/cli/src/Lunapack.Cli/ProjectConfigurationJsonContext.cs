using System.Text.Json.Serialization;

namespace Lunapack.Cli;

[JsonSourceGenerationOptions(
    Converters = [typeof(ProjectConfigurationSourceJsonConverter)],
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(ProjectConfiguration))]
internal partial class ProjectConfigurationJsonContext : JsonSerializerContext { }
