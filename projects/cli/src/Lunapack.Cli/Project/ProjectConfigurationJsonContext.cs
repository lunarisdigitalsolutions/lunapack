using System.Text.Json.Serialization;
using Lunapack.Cli.Application.Serialization;

namespace Lunapack.Cli.Project;

[JsonSourceGenerationOptions(
    Converters = [typeof(ProjectConfigurationSourceJsonConverter)],
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(ProjectConfiguration))]
internal partial class ProjectConfigurationJsonContext : JsonSerializerContext { }
