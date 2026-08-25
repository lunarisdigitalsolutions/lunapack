using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunapack.Cli;

internal static class LunapackJsonSerializerOptions
{
    public static JsonSerializerOptions Default { get; } =
        new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new ProjectConfigurationSourceJsonConverter() },
        };
}
