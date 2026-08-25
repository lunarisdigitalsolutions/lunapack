using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Lunapack.Cli;

internal sealed class ProjectConfigurationSourceYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(ProjectConfiguration.Source);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        parser.Consume<MappingStart>();

        string? path = null;
        string? reference = null;
        int? timeoutSeconds = null;
        string? sourceName = null;
        string? sourceType = null;
        string? url = null;
        while (!parser.Accept<MappingEnd>(out _))
        {
            var propertyName = parser.Consume<Scalar>().Value;
            var value = parser.Consume<Scalar>().Value;
            switch (propertyName)
            {
                case "name":
                    sourceName = value;
                    break;
                case "path":
                    path = value;
                    break;
                case "ref":
                    reference = value;
                    break;
                case "timeoutSeconds":
                    timeoutSeconds = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "type":
                    sourceType = value;
                    break;
                case "url":
                    url = value;
                    break;
            }
        }

        parser.Consume<MappingEnd>();
        return sourceType switch
        {
            "git" => new ProjectConfiguration.GitSource
            {
                Name = sourceName ?? string.Empty,
                Path = path,
                Ref = reference,
                TimeoutSeconds = timeoutSeconds,
                Url = url ?? string.Empty,
            },
            "local" => new ProjectConfiguration.LocalSource
            {
                Name = sourceName ?? string.Empty,
                Path = path ?? string.Empty,
            },
            _ => throw new YamlException("Project source type must be 'git' or 'local'."),
        };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        emitter.Emit(new MappingStart());
        switch (value)
        {
            case ProjectConfiguration.GitSource gitSource:
                Emit(emitter, "name", gitSource.Name);
                Emit(emitter, "type", gitSource.Type);
                Emit(emitter, "url", gitSource.Url);
                EmitOptional(emitter, "ref", gitSource.Ref);
                EmitOptional(emitter, "path", gitSource.Path);
                if (gitSource.TimeoutSeconds is { } timeoutSeconds)
                {
                    Emit(
                        emitter,
                        "timeoutSeconds",
                        timeoutSeconds.ToString(CultureInfo.InvariantCulture)
                    );
                }

                break;
            case ProjectConfiguration.LocalSource localSource:
                Emit(emitter, "name", localSource.Name);
                Emit(emitter, "type", localSource.Type);
                Emit(emitter, "path", localSource.Path);
                break;
            default:
                throw new YamlException(
                    $"Unsupported project source type '{value?.GetType().Name}'."
                );
        }

        emitter.Emit(new MappingEnd());
    }

    private static void Emit(IEmitter emitter, string propertyName, string value)
    {
        emitter.Emit(new Scalar(propertyName));
        emitter.Emit(new Scalar(value));
    }

    private static void EmitOptional(IEmitter emitter, string propertyName, string? value)
    {
        if (value is not null)
        {
            Emit(emitter, propertyName, value);
        }
    }
}
