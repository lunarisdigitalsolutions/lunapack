using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Lunapack.Cli;

internal sealed class ScalarValueDictionaryYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(Dictionary<string, object>);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        parser.Consume<MappingStart>();
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        while (!parser.Accept<MappingEnd>(out _))
        {
            var name = parser.Consume<Scalar>().Value;
            values.Add(name, ReadValue(parser, rootDeserializer));
        }

        parser.Consume<MappingEnd>();
        return values;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is not Dictionary<string, object> values)
        {
            throw new YamlException("Named scalar values are required.");
        }

        emitter.Emit(new MappingStart());
        foreach (var (name, item) in values)
        {
            emitter.Emit(new Scalar(name));
            serializer(item, item.GetType());
        }

        emitter.Emit(new MappingEnd());
    }

    private static object ReadValue(IParser parser, ObjectDeserializer rootDeserializer)
    {
        if (parser.Accept<SequenceStart>(out _))
        {
            return DeserializeRequired<List<string>>(rootDeserializer);
        }

        if (!parser.Accept<Scalar>(out var scalar) || scalar is null)
        {
            throw new YamlException("Named value must be a scalar or sequence.");
        }

        return scalar.Style == ScalarStyle.Plain && bool.TryParse(scalar.Value, out _)
            ? DeserializeRequired<bool>(rootDeserializer)
            : DeserializeRequired<string>(rootDeserializer);
    }

    private static T DeserializeRequired<T>(ObjectDeserializer rootDeserializer)
    {
        var value = rootDeserializer(typeof(T));
        return value is T typedValue
            ? typedValue
            : throw new YamlException("Named value has an invalid type.");
    }
}
