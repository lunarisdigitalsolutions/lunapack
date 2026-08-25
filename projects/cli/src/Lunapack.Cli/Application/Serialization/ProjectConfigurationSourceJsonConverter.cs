using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunapack.Cli;

internal sealed class ProjectConfigurationSourceJsonConverter
    : JsonConverter<ProjectConfiguration.Source>
{
    public override ProjectConfiguration.Source Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => throw new NotSupportedException("Project sources are read from YAML.");

    public override void Write(
        Utf8JsonWriter writer,
        ProjectConfiguration.Source source,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        switch (source)
        {
            case ProjectConfiguration.LocalSource localSource:
                writer.WriteString("type", localSource.Type);
                writer.WriteString("path", localSource.Path);
                break;
            case ProjectConfiguration.GitSource gitSource:
                writer.WriteString("type", gitSource.Type);
                writer.WriteString("url", gitSource.Url);
                WriteOptionalString(writer, "ref", gitSource.Ref);
                WriteOptionalString(writer, "path", gitSource.Path);
                if (gitSource.TimeoutSeconds is { } timeoutSeconds)
                {
                    writer.WriteNumber("timeoutSeconds", timeoutSeconds);
                }

                break;
            default:
                throw new JsonException(
                    $"Unsupported project source type '{source.GetType().Name}'."
                );
        }

        writer.WriteEndObject();
    }

    private static void WriteOptionalString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value
    )
    {
        if (value is not null)
        {
            writer.WriteString(propertyName, value);
        }
    }
}
