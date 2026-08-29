using System.Text.Json.Serialization;

namespace Lunapack.Cli.Project;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(ProjectLockFile))]
internal partial class ProjectLockFileJsonContext : JsonSerializerContext { }
