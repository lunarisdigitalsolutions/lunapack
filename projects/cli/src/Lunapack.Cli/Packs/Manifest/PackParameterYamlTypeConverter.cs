using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Lunapack.Cli.Packs.Manifest;

internal sealed class PackParameterYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(PackManifest.PackParameter);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        parser.Consume<MappingStart>();
        var parameter = new PackManifest.PackParameter { Type = string.Empty };
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        while (!parser.Accept<MappingEnd>(out _))
        {
            var propertyName = parser.Consume<Scalar>().Value;
            if (!propertyNames.Add(propertyName))
            {
                throw new YamlException($"Duplicate pack parameter property '{propertyName}'.");
            }

            switch (propertyName)
            {
                case "default":
                    parameter.Default = ReadValue(parser, rootDeserializer);
                    break;
                case "description":
                    parameter.Description = (string?)rootDeserializer(typeof(string));
                    break;
                case "displayName":
                    parameter.DisplayName = (string?)rootDeserializer(typeof(string));
                    break;
                case "multiple":
                    parameter.Multiple = DeserializeRequired<bool>(rootDeserializer, propertyName);
                    break;
                case "required":
                    parameter.Required = DeserializeRequired<bool>(rootDeserializer, propertyName);
                    break;
                case "type":
                    parameter.Type = DeserializeRequired<string>(rootDeserializer, propertyName);
                    break;
                case "values":
                    parameter.Values = DeserializeRequired<List<string>>(
                        rootDeserializer,
                        propertyName
                    );
                    break;
                default:
                    throw new YamlException($"Unknown pack parameter property '{propertyName}'.");
            }
        }

        parser.Consume<MappingEnd>();
        return parameter;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is not PackManifest.PackParameter parameter)
        {
            throw new YamlException("Pack parameter value is required.");
        }

        emitter.Emit(new MappingStart());
        EmitOptional(emitter, serializer, "default", parameter.Default);
        EmitOptional(emitter, serializer, "description", parameter.Description);
        EmitOptional(emitter, serializer, "displayName", parameter.DisplayName);
        EmitOptional(emitter, serializer, "multiple", parameter.Multiple);
        Emit(emitter, serializer, "required", parameter.Required);
        Emit(emitter, serializer, "type", parameter.Type);
        EmitOptional(emitter, serializer, "values", parameter.Values);
        emitter.Emit(new MappingEnd());
    }

    private static object ReadValue(IParser parser, ObjectDeserializer rootDeserializer)
    {
        if (parser.Accept<SequenceStart>(out _))
        {
            return DeserializeRequired<List<string>>(rootDeserializer, "default");
        }

        if (!parser.Accept<Scalar>(out var scalar) || scalar is null)
        {
            throw new YamlException("Pack parameter default must be a scalar or sequence.");
        }

        return scalar.Style == ScalarStyle.Plain && bool.TryParse(scalar.Value, out _)
            ? DeserializeRequired<bool>(rootDeserializer, "default")
            : DeserializeRequired<string>(rootDeserializer, "default");
    }

    private static T DeserializeRequired<T>(
        ObjectDeserializer rootDeserializer,
        string propertyName
    )
    {
        var value = rootDeserializer(typeof(T));
        return value is T typedValue
            ? typedValue
            : throw new YamlException($"Pack parameter property '{propertyName}' is invalid.");
    }

    private static void EmitOptional(
        IEmitter emitter,
        ObjectSerializer serializer,
        string propertyName,
        object? value
    )
    {
        if (value is not null)
        {
            Emit(emitter, serializer, propertyName, value);
        }
    }

    private static void Emit(
        IEmitter emitter,
        ObjectSerializer serializer,
        string propertyName,
        object value
    )
    {
        emitter.Emit(new Scalar(propertyName));
        serializer(value, value.GetType());
    }
}
