using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Serialization;

namespace Lunapack.Cli.Packs.Planning;

internal static class JsonManagedFileMerger
{
    private static readonly UTF8Encoding _utf8 = new(false, true);
    private static readonly JsonSerializerOptions _options = new(
        LunapackJsonSerializerOptions.Default
    )
    {
        WriteIndented = true,
    };

    public static ManifestOperationResult<byte[]> Merge(
        byte[] targetContents,
        byte[] sourceContents
    )
    {
        try
        {
            var target = JsonNode.Parse(_utf8.GetString(targetContents));
            var source = JsonNode.Parse(_utf8.GetString(sourceContents));
            JsonNode? merged = (target, source) switch
            {
                (JsonObject targetObject, JsonObject sourceObject) => MergeObjects(
                    targetObject,
                    sourceObject
                ),
                (JsonArray targetArray, JsonArray sourceArray) => MergeArrays(
                    targetArray,
                    sourceArray
                ),
                _ => null,
            };
            if (merged is null)
            {
                return ManifestOperationResult<byte[]>.Failure(
                    "JSON merge requires source and target JSON objects or arrays of the same kind."
                );
            }

            return ManifestOperationResult<byte[]>.Success(
                _utf8.GetBytes(merged.ToJsonString(_options))
            );
        }
        catch (Exception exception) when (exception is DecoderFallbackException or JsonException)
        {
            return ManifestOperationResult<byte[]>.Failure(
                $"JSON merge requires valid UTF-8 JSON: {exception.Message}"
            );
        }
    }

    private static JsonArray MergeArrays(JsonArray target, JsonArray source)
    {
        foreach (var sourceValue in source)
        {
            if (!target.Any(targetValue => JsonNode.DeepEquals(targetValue, sourceValue)))
            {
                target.Add(sourceValue?.DeepClone());
            }
        }

        return target;
    }

    private static JsonObject MergeObjects(JsonObject target, JsonObject source)
    {
        foreach (var (key, sourceValue) in source)
        {
            target.TryGetPropertyValue(key, out var targetValue);
            target[key] = MergeValues(targetValue, sourceValue);
        }

        return target;
    }

    private static JsonNode? MergeValues(JsonNode? targetValue, JsonNode? sourceValue) =>
        (targetValue, sourceValue) switch
        {
            (JsonObject targetObject, JsonObject sourceObject) => MergeObjects(
                targetObject,
                sourceObject
            ),
            (JsonArray targetArray, JsonArray sourceArray) => MergeArrays(targetArray, sourceArray),
            _ => sourceValue?.DeepClone(),
        };
}
