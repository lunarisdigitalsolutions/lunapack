using System.Text.Json.Serialization;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Sources.Oci;

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
[JsonSerializable(typeof(OciPackArtifact.OciPackConfig))]
[JsonSerializable(typeof(OciPackArtifact.OciPackImageManifest))]
internal partial class LunapackJsonContext : JsonSerializerContext { }
