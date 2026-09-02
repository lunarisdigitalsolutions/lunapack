using System.Text.Json.Serialization;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Application.Serialization;

[JsonSourceGenerationOptions(
    Converters = [typeof(ScalarValueJsonConverter)],
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(ProjectManifest))]
[JsonSerializable(typeof(PackManifest))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(GitSourceCacheEntry))]
[JsonSerializable(typeof(GitLinkCacheMetadata))]
internal partial class LunapackJsonContext : JsonSerializerContext { }
