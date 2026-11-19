using System.Text.Json;

namespace Lunapack.Cli.Sbom;

internal static class Spdx23SbomWriter
{
    private const string Disclaimer =
        "This is a lock-file inventory, not a signature or attestation.";

    public static void Write(
        Utf8JsonWriter writer,
        SbomInventory inventory,
        SbomTarget target,
        DateTimeOffset created
    )
    {
        var fingerprint = SbomInventoryFingerprint.CreateDocument(inventory, target, created);
        var packageIds = PackageIds(inventory);
        var fileIds = FileIds(inventory);

        writer.WriteStartObject();
        writer.WriteString("spdxVersion", $"SPDX-{target.Version}");
        writer.WriteString("dataLicense", "CC0-1.0");
        writer.WriteString("SPDXID", "SPDXRef-DOCUMENT");
        writer.WriteString("name", "LunaPack dependency inventory");
        writer.WriteString(
            "documentNamespace",
            $"https://lunapack.dev/sbom/{target.Name}/{fingerprint}"
        );
        writer.WriteString("comment", Disclaimer);
        WriteCreationInfo(writer, created);
        WritePackages(writer, inventory);
        WriteFiles(writer, inventory);
        WriteDocumentDescribes(writer, packageIds, fileIds);
        WriteRelationships(writer, BuildRelationships(inventory, packageIds));
        writer.WriteEndObject();
    }

    private static void WriteCreationInfo(Utf8JsonWriter writer, DateTimeOffset created)
    {
        writer.WritePropertyName("creationInfo");
        writer.WriteStartObject();
        writer.WriteString("created", SbomTimestamp.Format(created));
        writer.WritePropertyName("creators");
        writer.WriteStartArray();
        writer.WriteStringValue("Tool: Luna");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePackages(Utf8JsonWriter writer, SbomInventory inventory)
    {
        writer.WritePropertyName("packages");
        writer.WriteStartArray();
        foreach (var pack in inventory.Packs)
        {
            WritePackage(
                writer,
                SbomIdentifiers.Spdx2ElementId("Package", pack.Reference),
                pack.Id,
                pack.Version,
                pack.Reference,
                SbomEvidenceFormatter.ForPack(pack)
            );
        }

        foreach (var link in inventory.Links)
        {
            WritePackage(
                writer,
                SbomIdentifiers.Spdx2ElementId("Link", link.Reference),
                $"Luna Link: {link.Name}",
                null,
                null,
                SbomEvidenceFormatter.ForLink(link)
            );
        }

        writer.WriteEndArray();
    }

    private static void WritePackage(
        Utf8JsonWriter writer,
        string id,
        string name,
        string? version,
        string? packageUrl,
        IReadOnlyList<KeyValuePair<string, string>> evidence
    )
    {
        writer.WriteStartObject();
        writer.WriteString("SPDXID", id);
        writer.WriteString("name", name);
        writer.WriteString("downloadLocation", "NOASSERTION");
        writer.WriteBoolean("filesAnalyzed", false);
        writer.WriteString("licenseConcluded", "NOASSERTION");
        writer.WriteString("licenseDeclared", "NOASSERTION");
        writer.WriteString("copyrightText", "NOASSERTION");
        if (version is not null)
        {
            writer.WriteString("versionInfo", version);
        }

        if (packageUrl is not null)
        {
            writer.WritePropertyName("externalRefs");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("referenceCategory", "PACKAGE-MANAGER");
            writer.WriteString("referenceType", "purl");
            writer.WriteString("referenceLocator", packageUrl);
            writer.WriteEndObject();
            writer.WriteEndArray();
        }

        WriteEvidence(writer, "sourceInfo", evidence);
        writer.WriteEndObject();
    }

    private static void WriteFiles(Utf8JsonWriter writer, SbomInventory inventory)
    {
        writer.WritePropertyName("files");
        writer.WriteStartArray();
        foreach (var file in Files(inventory))
        {
            writer.WriteStartObject();
            writer.WriteString("SPDXID", SbomIdentifiers.Spdx2ElementId("File", file.Reference));
            writer.WriteString("fileName", file.TargetPath);
            writer.WritePropertyName("checksums");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("algorithm", "SHA256");
            writer.WriteString("checksumValue", file.Sha256.ToLowerInvariant());
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteString("licenseConcluded", "NOASSERTION");
            writer.WritePropertyName("licenseInfoInFiles");
            writer.WriteStartArray();
            writer.WriteStringValue("NOASSERTION");
            writer.WriteEndArray();
            writer.WriteString("copyrightText", "NOASSERTION");
            writer.WritePropertyName("fileTypes");
            writer.WriteStartArray();
            writer.WriteStringValue("OTHER");
            writer.WriteEndArray();
            WriteEvidence(writer, "comment", SbomEvidenceFormatter.ForFile(file));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteEvidence(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<KeyValuePair<string, string>> evidence
    )
    {
        if (SbomEvidenceFormatter.ForSpdx(evidence) is { } formatted)
        {
            writer.WriteString(propertyName, formatted);
        }
    }

    private static void WriteDocumentDescribes(
        Utf8JsonWriter writer,
        Dictionary<string, string> packageIds,
        Dictionary<string, string> fileIds
    )
    {
        writer.WritePropertyName("documentDescribes");
        writer.WriteStartArray();
        foreach (var id in packageIds.Values.Concat(fileIds.Values).Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(id);
        }

        writer.WriteEndArray();
    }

    private static void WriteRelationships(
        Utf8JsonWriter writer,
        IReadOnlyList<SpdxRelationship> relationships
    )
    {
        writer.WritePropertyName("relationships");
        writer.WriteStartArray();
        foreach (var relationship in relationships)
        {
            writer.WriteStartObject();
            writer.WriteString("spdxElementId", relationship.From);
            writer.WriteString("relationshipType", relationship.Type);
            writer.WriteString("relatedSpdxElement", relationship.To);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static SpdxRelationship[] BuildRelationships(
        SbomInventory inventory,
        Dictionary<string, string> packageIds
    )
    {
        var relationships = new List<SpdxRelationship>();
        foreach (var pack in inventory.Packs)
        {
            var fromId = packageIds[pack.Reference];
            foreach (var dependency in pack.Dependencies)
            {
                relationships.Add(
                    new SpdxRelationship(fromId, "DEPENDS_ON", packageIds[dependency.Reference])
                );
            }
        }

        return
        [
            .. relationships
                .OrderBy(relationship => relationship.From, StringComparer.Ordinal)
                .ThenBy(relationship => relationship.Type, StringComparer.Ordinal)
                .ThenBy(relationship => relationship.To, StringComparer.Ordinal),
        ];
    }

    private static Dictionary<string, string> PackageIds(SbomInventory inventory)
    {
        var packageIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pack in inventory.Packs)
        {
            packageIds.Add(
                pack.Reference,
                SbomIdentifiers.Spdx2ElementId("Package", pack.Reference)
            );
        }

        foreach (var link in inventory.Links)
        {
            packageIds.Add(link.Reference, SbomIdentifiers.Spdx2ElementId("Link", link.Reference));
        }

        return packageIds;
    }

    private static Dictionary<string, string> FileIds(SbomInventory inventory) =>
        Files(inventory)
            .ToDictionary(
                file => file.Reference,
                file => SbomIdentifiers.Spdx2ElementId("File", file.Reference),
                StringComparer.Ordinal
            );

    private static IEnumerable<SbomInventory.ManagedFile> Files(SbomInventory inventory) =>
        inventory
            .Packs.SelectMany(pack => pack.Files)
            .Concat(inventory.Links.SelectMany(link => link.Files))
            .OrderBy(file => file.Reference, StringComparer.Ordinal);

    private sealed record SpdxRelationship(string From, string Type, string To);
}
