using System.Text.Json;

namespace Lunapack.Cli.Sbom;

internal static class Spdx301SbomWriter
{
    private const string Disclaimer =
        "This is a lock-file inventory, not a signature or attestation.";
    private const string SoftwareAgentName = "Luna";

    public static void Write(
        Utf8JsonWriter writer,
        SbomInventory inventory,
        SbomTarget target,
        DateTimeOffset created
    )
    {
        var fingerprint = SbomInventoryFingerprint.CreateDocument(inventory, target, created);
        var documentId = SbomIdentifiers.Spdx3DocumentId(fingerprint);
        var creationInfoId = SbomIdentifiers.Spdx3CreationInfoId(fingerprint);
        var agentId = SbomIdentifiers.Spdx3ElementId("agent", SoftwareAgentName);
        var packageIds = PackageIds(inventory);
        var files = Files(inventory);
        var relationships = BuildRelationships(inventory, packageIds, files);
        var elementIds = packageIds
            .Values.Concat(files.Select(file => file.Id))
            .Concat(relationships.Select(relationship => relationship.Id))
            .Append(agentId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        writer.WriteStartObject();
        writer.WriteString("@context", "https://spdx.org/rdf/3.0.1/spdx-context.jsonld");
        writer.WritePropertyName("@graph");
        writer.WriteStartArray();
        WriteCreationInfo(writer, creationInfoId, agentId, target, created);
        WriteDocument(writer, documentId, creationInfoId, elementIds);
        WriteSoftwareAgent(writer, agentId, creationInfoId);
        WritePackages(writer, inventory, packageIds, creationInfoId);
        WriteFiles(writer, files, creationInfoId);
        WriteHashes(writer, files);
        WriteRelationships(writer, relationships, creationInfoId);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCreationInfo(
        Utf8JsonWriter writer,
        string creationInfoId,
        string agentId,
        SbomTarget target,
        DateTimeOffset created
    )
    {
        writer.WriteStartObject();
        writer.WriteString("@id", creationInfoId);
        writer.WriteString("type", "CreationInfo");
        writer.WriteString("specVersion", target.Version);
        writer.WriteString("created", SbomTimestamp.Format(created));
        writer.WritePropertyName("createdBy");
        writer.WriteStartArray();
        writer.WriteStringValue(agentId);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteDocument(
        Utf8JsonWriter writer,
        string documentId,
        string creationInfoId,
        IReadOnlyList<string> elementIds
    )
    {
        writer.WriteStartObject();
        writer.WriteString("type", "SpdxDocument");
        writer.WriteString("spdxId", documentId);
        writer.WriteString("creationInfo", creationInfoId);
        writer.WriteString("name", "LunaPack dependency inventory");
        writer.WriteString("comment", Disclaimer);
        writer.WritePropertyName("profileConformance");
        writer.WriteStartArray();
        writer.WriteStringValue("core");
        writer.WriteStringValue("software");
        writer.WriteEndArray();
        writer.WritePropertyName("element");
        writer.WriteStartArray();
        foreach (var id in elementIds)
        {
            writer.WriteStringValue(id);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSoftwareAgent(
        Utf8JsonWriter writer,
        string agentId,
        string creationInfoId
    )
    {
        writer.WriteStartObject();
        writer.WriteString("type", "SoftwareAgent");
        writer.WriteString("spdxId", agentId);
        writer.WriteString("creationInfo", creationInfoId);
        writer.WriteString("name", SoftwareAgentName);
        writer.WriteEndObject();
    }

    private static void WritePackages(
        Utf8JsonWriter writer,
        SbomInventory inventory,
        Dictionary<string, string> packageIds,
        string creationInfoId
    )
    {
        foreach (var pack in inventory.Packs)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "software_Package");
            writer.WriteString("spdxId", packageIds[pack.Reference]);
            writer.WriteString("creationInfo", creationInfoId);
            writer.WriteString("name", pack.Id);
            writer.WriteString("software_packageVersion", pack.Version);
            writer.WriteString("software_packageUrl", pack.Reference);
            WriteEvidence(writer, "software_sourceInfo", SbomEvidenceFormatter.ForPack(pack));
            writer.WriteEndObject();
        }

        foreach (var link in inventory.Links)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "software_Package");
            writer.WriteString("spdxId", packageIds[link.Reference]);
            writer.WriteString("creationInfo", creationInfoId);
            writer.WriteString("name", $"Luna Link: {link.Name}");
            WriteEvidence(writer, "software_sourceInfo", SbomEvidenceFormatter.ForLink(link));
            writer.WriteEndObject();
        }
    }

    private static void WriteFiles(
        Utf8JsonWriter writer,
        IReadOnlyList<ManagedFileElement> files,
        string creationInfoId
    )
    {
        foreach (var file in files)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "software_File");
            writer.WriteString("spdxId", file.Id);
            writer.WriteString("creationInfo", creationInfoId);
            writer.WriteString("name", file.File.TargetPath);
            writer.WritePropertyName("verifiedUsing");
            writer.WriteStartArray();
            writer.WriteStringValue(file.HashId);
            writer.WriteEndArray();
            WriteEvidence(writer, "comment", SbomEvidenceFormatter.ForFile(file.File));
            writer.WriteEndObject();
        }
    }

    private static void WriteHashes(Utf8JsonWriter writer, IReadOnlyList<ManagedFileElement> files)
    {
        foreach (var file in files)
        {
            writer.WriteStartObject();
            writer.WriteString("@id", file.HashId);
            writer.WriteString("type", "Hash");
            writer.WriteString("algorithm", "sha256");
            writer.WriteString("hashValue", file.File.Sha256.ToLowerInvariant());
            writer.WriteEndObject();
        }
    }

    private static void WriteRelationships(
        Utf8JsonWriter writer,
        IReadOnlyList<SpdxRelationship> relationships,
        string creationInfoId
    )
    {
        foreach (var relationship in relationships)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "Relationship");
            writer.WriteString("spdxId", relationship.Id);
            writer.WriteString("creationInfo", creationInfoId);
            writer.WriteString("from", relationship.From);
            writer.WritePropertyName("to");
            writer.WriteStartArray();
            writer.WriteStringValue(relationship.To);
            writer.WriteEndArray();
            writer.WriteString("relationshipType", relationship.Type);
            writer.WriteEndObject();
        }
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

    private static Dictionary<string, string> PackageIds(SbomInventory inventory)
    {
        var packageIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pack in inventory.Packs)
        {
            packageIds.Add(
                pack.Reference,
                SbomIdentifiers.Spdx3ElementId("package", pack.Reference)
            );
        }

        foreach (var link in inventory.Links)
        {
            packageIds.Add(link.Reference, SbomIdentifiers.Spdx3ElementId("link", link.Reference));
        }

        return packageIds;
    }

    private static ManagedFileElement[] Files(SbomInventory inventory) =>
        [
            .. inventory
                .Packs.SelectMany(pack => pack.Files)
                .Concat(inventory.Links.SelectMany(link => link.Files))
                .OrderBy(file => file.Reference, StringComparer.Ordinal)
                .Select(file => new ManagedFileElement(
                    file,
                    SbomIdentifiers.Spdx3ElementId("file", file.Reference),
                    SbomIdentifiers.Spdx3ElementId(
                        "hash",
                        $"{file.Reference}\0sha256\0{file.Sha256}"
                    )
                )),
        ];

    private static SpdxRelationship[] BuildRelationships(
        SbomInventory inventory,
        Dictionary<string, string> packageIds,
        IReadOnlyList<ManagedFileElement> files
    )
    {
        var relationships = new List<SpdxRelationship>();
        foreach (var pack in inventory.Packs)
        {
            foreach (var dependency in pack.Dependencies)
            {
                relationships.Add(
                    CreateRelationship(
                        packageIds[pack.Reference],
                        "dependsOn",
                        packageIds[dependency.Reference]
                    )
                );
            }
        }

        foreach (var file in files)
        {
            relationships.Add(
                CreateRelationship(packageIds[file.File.OwnerReference], "contains", file.Id)
            );
        }

        return
        [
            .. relationships
                .OrderBy(relationship => relationship.From, StringComparer.Ordinal)
                .ThenBy(relationship => relationship.Type, StringComparer.Ordinal)
                .ThenBy(relationship => relationship.To, StringComparer.Ordinal),
        ];
    }

    private static SpdxRelationship CreateRelationship(string from, string type, string to) =>
        new(
            SbomIdentifiers.Spdx3ElementId("relationship", $"{from}\0{type}\0{to}"),
            from,
            type,
            to
        );

    private sealed record ManagedFileElement(
        SbomInventory.ManagedFile File,
        string Id,
        string HashId
    );

    private sealed record SpdxRelationship(string Id, string From, string Type, string To);
}
