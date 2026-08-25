using System.Text.Json.Serialization;

namespace Lunapack.Cli;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(ProjectManifest))]
[JsonSerializable(typeof(PackManifest))]
[JsonSerializable(typeof(GitSourceCacheEntry))]
internal partial class LunapackJsonContext : JsonSerializerContext { }
