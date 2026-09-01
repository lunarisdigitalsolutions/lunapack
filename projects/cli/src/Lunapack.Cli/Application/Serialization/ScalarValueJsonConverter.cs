using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunapack.Cli.Application.Serialization;

internal sealed class ScalarValueJsonConverter : JsonConverter<object>
{
    public override object Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => ReadValue(ref reader);

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case string stringValue:
                writer.WriteStringValue(stringValue);
                break;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                break;
            case IEnumerable<object> values:
                writer.WriteStartArray();
                foreach (var item in values)
                {
                    Write(writer, item, options);
                }

                writer.WriteEndArray();
                break;
            default:
                throw new JsonException($"Unsupported scalar value type '{value.GetType()}'.");
        }
    }

    private static object ReadValue(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString()
                ?? throw new JsonException("Scalar string value is null."),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.StartArray => ReadArray(ref reader),
            _ => throw new JsonException($"Unsupported scalar JSON token '{reader.TokenType}'."),
        };

    private static List<object> ReadArray(ref Utf8JsonReader reader)
    {
        var values = new List<object>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            values.Add(ReadValue(ref reader));
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("Scalar value array is incomplete.");
        }

        return values;
    }
}
