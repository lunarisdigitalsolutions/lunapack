using System.Text.Json;

namespace Lunapack.Cli.Sbom;

internal static class CycloneDxSbomWriter
{
    private const string Disclaimer =
        "This is a lock-file inventory, not a signature or attestation.";

    public static void Write(Utf8JsonWriter writer, SbomInventory inventory, SbomTarget target)
    {
        writer.WriteStartObject();
        writer.WriteString("bomFormat", "CycloneDX");
        writer.WriteString("specVersion", target.Version);
        writer.WriteNumber("version", 1);
        WriteMetadata(writer);
        WriteComponents(writer, inventory);
        WriteDependencies(writer, inventory);
        writer.WriteEndObject();
    }

    private static void WriteMetadata(Utf8JsonWriter writer)
    {
        writer.WritePropertyName("metadata");
        writer.WriteStartObject();
        writer.WritePropertyName("properties");
        writer.WriteStartArray();
        WriteProperty(writer, "lunapack:disclaimer", Disclaimer);
        WriteProperty(writer, "lunapack:inventory-source", "validated lunapack-lock.yml");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteComponents(Utf8JsonWriter writer, SbomInventory inventory)
    {
        writer.WritePropertyName("components");
        writer.WriteStartArray();
        foreach (var pack in inventory.Packs)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "library");
            writer.WriteString("bom-ref", pack.Reference);
            writer.WriteString("name", pack.Id);
            writer.WriteString("version", pack.Version);
            writer.WriteString("purl", pack.Reference);
            WriteProperties(writer, SbomEvidenceFormatter.ForPack(pack));
            writer.WriteEndObject();
        }

        foreach (var link in inventory.Links)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "library");
            writer.WriteString("bom-ref", link.Reference);
            writer.WriteString("name", link.Name);
            WriteProperties(writer, SbomEvidenceFormatter.ForLink(link));
            writer.WriteEndObject();
        }

        foreach (var file in Files(inventory))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "file");
            writer.WriteString("bom-ref", file.Reference);
            writer.WriteString("name", file.TargetPath);
            writer.WritePropertyName("hashes");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("alg", "SHA-256");
            writer.WriteString("content", file.Sha256.ToLowerInvariant());
            writer.WriteEndObject();
            writer.WriteEndArray();
            WriteProperties(writer, SbomEvidenceFormatter.ForFile(file));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteDependencies(Utf8JsonWriter writer, SbomInventory inventory)
    {
        var dependencies = new SortedDictionary<string, IReadOnlyList<string>>(
            StringComparer.Ordinal
        );
        foreach (var pack in inventory.Packs)
        {
            dependencies.Add(
                pack.Reference,
                [.. pack.Dependencies.Select(dependency => dependency.Reference)]
            );
        }

        foreach (var link in inventory.Links)
        {
            dependencies.Add(link.Reference, []);
        }

        foreach (var file in Files(inventory))
        {
            dependencies.Add(file.Reference, []);
        }

        writer.WritePropertyName("dependencies");
        writer.WriteStartArray();
        foreach (var dependency in dependencies)
        {
            writer.WriteStartObject();
            writer.WriteString("ref", dependency.Key);
            writer.WritePropertyName("dependsOn");
            writer.WriteStartArray();
            foreach (var reference in dependency.Value.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(reference);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteProperties(
        Utf8JsonWriter writer,
        IReadOnlyList<KeyValuePair<string, string>> properties
    )
    {
        if (properties.Count == 0)
        {
            return;
        }

        writer.WritePropertyName("properties");
        writer.WriteStartArray();
        foreach (var property in properties)
        {
            WriteProperty(writer, $"lunapack:{property.Key}", property.Value);
        }

        writer.WriteEndArray();
    }

    private static void WriteProperty(Utf8JsonWriter writer, string name, string value)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("value", value);
        writer.WriteEndObject();
    }

    private static IEnumerable<SbomInventory.ManagedFile> Files(SbomInventory inventory) =>
        inventory
            .Packs.SelectMany(pack => pack.Files)
            .Concat(inventory.Links.SelectMany(link => link.Files))
            .OrderBy(file => file.Reference, StringComparer.Ordinal);
}
