using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Lunapack.Cli.Project;

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
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        while (!parser.Accept<MappingEnd>(out _))
        {
            var propertyName = parser.Consume<Scalar>().Value;
            if (!propertyNames.Add(propertyName))
            {
                throw new YamlException($"Duplicate project source property '{propertyName}'.");
            }

            switch (propertyName)
            {
                case "name":
                    sourceName = parser.Consume<Scalar>().Value;
                    break;
                case "path":
                    path = parser.Consume<Scalar>().Value;
                    break;
                case "ref":
                    reference = parser.Consume<Scalar>().Value;
                    break;
                case "timeoutSeconds":
                    if (
                        !int.TryParse(
                            parser.Consume<Scalar>().Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var parsedTimeoutSeconds
                        )
                    )
                    {
                        throw new YamlException(
                            "Project source timeoutSeconds must be an integer."
                        );
                    }

                    timeoutSeconds = parsedTimeoutSeconds;
                    break;
                case "type":
                    sourceType = parser.Consume<Scalar>().Value;
                    break;
                case "url":
                    url = parser.Consume<Scalar>().Value;
                    break;
                default:
                    parser.SkipThisAndNestedEvents();
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
