using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Lunapack.Cli;

internal sealed class PackParameterYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(PackManifest.PackParameter);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        parser.Consume<MappingStart>();
        var parameter = new PackManifest.PackParameter { Type = string.Empty };
        while (!parser.Accept<MappingEnd>(out _))
        {
            var propertyName = parser.Consume<Scalar>().Value;
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
                    parameter.Multiple = (bool?)rootDeserializer(typeof(bool));
                    break;
                case "required":
                    parameter.Required = (bool)rootDeserializer(typeof(bool));
                    break;
                case "type":
                    parameter.Type = (string)rootDeserializer(typeof(string));
                    break;
                case "values":
                    parameter.Values = (List<string>)rootDeserializer(typeof(List<string>));
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
            return rootDeserializer(typeof(List<string>));
        }

        var scalar = parser.Peek<Scalar>();
        return scalar.Style == ScalarStyle.Plain && bool.TryParse(scalar.Value, out _)
            ? rootDeserializer(typeof(bool))
            : rootDeserializer(typeof(string));
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
