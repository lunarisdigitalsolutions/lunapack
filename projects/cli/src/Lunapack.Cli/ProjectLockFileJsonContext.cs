using System.Text.Json.Serialization;

namespace Lunapack.Cli;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(ProjectLockFile))]
internal partial class ProjectLockFileJsonContext : JsonSerializerContext { }
