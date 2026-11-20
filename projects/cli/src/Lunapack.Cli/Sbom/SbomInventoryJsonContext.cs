using System.Text.Json.Serialization;

namespace Lunapack.Cli.Sbom;

[JsonSerializable(typeof(SbomInventory))]
internal partial class SbomInventoryJsonContext : JsonSerializerContext { }
