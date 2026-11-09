using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Lunapack.Cli.Project;

internal sealed class ProjectConfigurationSourceYamlTypeConverter : IYamlTypeConverter
{
    private static readonly HashSet<string> _credentialPropertyNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "auth",
        "accessToken",
        "clientSecret",
        "credential",
        "credentials",
        "password",
        "refreshToken",
        "secret",
        "token",
    };

    public bool Accepts(Type type) => type == typeof(ProjectConfiguration.Source);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        parser.Consume<MappingStart>();
        return CreateSource(ReadSourceProperties(parser));
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
            case ProjectConfiguration.OciSource ociSource:
                Emit(emitter, "name", ociSource.Name);
                Emit(emitter, "type", ociSource.Type);
                Emit(emitter, "url", ociSource.Url);
                EmitOptional(emitter, "path", ociSource.Path);
                EmitOptional(emitter, "username", ociSource.Username);
                break;
            default:
                throw new YamlException(
                    $"Unsupported project source type '{value?.GetType().Name}'."
                );
        }

        emitter.Emit(new MappingEnd());
    }

    private static (
        string? Path,
        string? Reference,
        int? TimeoutSeconds,
        string? Name,
        string? Type,
        string? Url,
        string? Username
    ) ReadSourceProperties(IParser parser)
    {
        string? path = null;
        string? reference = null;
        int? timeoutSeconds = null;
        string? sourceName = null;
        string? sourceType = null;
        string? url = null;
        string? username = null;
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        while (!parser.Accept<MappingEnd>(out _))
        {
            var propertyName = parser.Consume<Scalar>().Value;
            if (!propertyNames.Add(propertyName))
            {
                throw new YamlException($"Duplicate project source property '{propertyName}'.");
            }

            if (_credentialPropertyNames.Contains(propertyName))
            {
                throw new YamlException(
                    "Registry credential material must not be stored in project configuration."
                );
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
                    timeoutSeconds = ReadTimeoutSeconds(parser);
                    break;
                case "type":
                    sourceType = parser.Consume<Scalar>().Value;
                    break;
                case "url":
                    url = parser.Consume<Scalar>().Value;
                    break;
                case "username":
                    username = parser.Consume<Scalar>().Value;
                    break;
                default:
                    parser.SkipThisAndNestedEvents();
                    break;
            }
        }

        parser.Consume<MappingEnd>();
        return (path, reference, timeoutSeconds, sourceName, sourceType, url, username);
    }

    private static int ReadTimeoutSeconds(IParser parser)
    {
        if (
            !int.TryParse(
                parser.Consume<Scalar>().Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedTimeoutSeconds
            )
        )
        {
            throw new YamlException("Project source timeoutSeconds must be an integer.");
        }

        return parsedTimeoutSeconds;
    }

    private static ProjectConfiguration.Source CreateSource(
        (
            string? Path,
            string? Reference,
            int? TimeoutSeconds,
            string? Name,
            string? Type,
            string? Url,
            string? Username
        ) source
    )
    {
        var hasGitOnlyProperties =
            source.Reference is not null || source.TimeoutSeconds is not null;
        if (
            source.Username is not null
            && !string.Equals(source.Type, "oci", StringComparison.Ordinal)
        )
        {
            throw new YamlException("Source username is supported only for OCI sources.");
        }

        if (string.Equals(source.Type, "oci", StringComparison.Ordinal) && hasGitOnlyProperties)
        {
            throw new YamlException(
                "OCI sources do not support Git ref or timeoutSeconds properties."
            );
        }

        if (
            string.Equals(source.Type, "local", StringComparison.Ordinal)
            && (source.Url is not null || hasGitOnlyProperties)
        )
        {
            throw new YamlException(
                "Local sources do not support URL, ref, or timeoutSeconds properties."
            );
        }

        return source.Type switch
        {
            "git" => new ProjectConfiguration.GitSource
            {
                Name = source.Name ?? string.Empty,
                Path = source.Path,
                Ref = source.Reference,
                TimeoutSeconds = source.TimeoutSeconds,
                Url = source.Url ?? string.Empty,
            },
            "local" => new ProjectConfiguration.LocalSource
            {
                Name = source.Name ?? string.Empty,
                Path = source.Path ?? string.Empty,
            },
            "oci" => new ProjectConfiguration.OciSource
            {
                Name = source.Name ?? string.Empty,
                Path = source.Path,
                Url = source.Url ?? string.Empty,
                Username = source.Username,
            },
            _ => throw new YamlException("Project source type must be 'git', 'local', or 'oci'."),
        };
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
