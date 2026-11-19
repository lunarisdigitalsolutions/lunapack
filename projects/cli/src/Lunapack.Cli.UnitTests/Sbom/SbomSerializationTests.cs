using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Lunapack.Cli.Sbom;

namespace Lunapack.Cli.UnitTests.Sbom;

public sealed class SbomSerializationTests
{
    [Test]
    [Arguments("cyclonedx-1.6")]
    [Arguments("cyclonedx-1.7")]
    [Arguments("spdx-2.3")]
    [Arguments("spdx-3.0.1")]
    public async Task Serialize_WhenInventoryContainsDependenciesAndHashes_ValidatesAgainstSchema(
        string targetName
    )
    {
        var target =
            SbomTarget.Parse(targetName)
            ?? throw new InvalidOperationException($"Unknown test target '{targetName}'.");
        var inventory = CreateInventory();
        var json = SbomDocumentSerializer.Serialize(
            inventory,
            target,
            new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero).AddMilliseconds(100)
        );
        using var document = JsonDocument.Parse(json);
        var schemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "SbomSchemas",
            $"{targetName}.json"
        );
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath));
        var result = schema.Evaluate(
            document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List }
        );

        await Assert.That(result.IsValid).IsTrue().Because(DescribeErrors(targetName, result));
    }

    [Test]
    public async Task Serialize_WhenTimestampHasOffset_NormalizesSpdxCreationTimeToUtc()
    {
        var inventory = CreateInventory();
        var target = SbomTarget.Parse("spdx-2.3")!;
        var created = new DateTimeOffset(2025, 6, 7, 10, 9, 10, TimeSpan.FromHours(2));

        using var document = JsonDocument.Parse(
            SbomDocumentSerializer.Serialize(inventory, target, created)
        );

        await Assert
            .That(
                document.RootElement.GetProperty("creationInfo").GetProperty("created").GetString()
            )
            .IsEqualTo("2025-06-07T08:09:10Z");
    }

    [Test]
    public async Task Serialize_WhenCycloneDxTargetSelected_IsDeterministicAndOmitsTimestamp()
    {
        var inventory = CreateInventory();
        var target = SbomTarget.Parse("cyclonedx-1.7")!;

        var first = SbomDocumentSerializer.Serialize(
            inventory,
            target,
            new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero)
        );
        var second = SbomDocumentSerializer.Serialize(
            inventory,
            target,
            new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero).AddMilliseconds(900)
        );
        using var document = JsonDocument.Parse(first);

        await Assert.That(first).IsEqualTo(second);
        await Assert
            .That(document.RootElement.GetProperty("metadata").TryGetProperty("timestamp", out _))
            .IsFalse();
    }

    [Test]
    [Arguments("spdx-2.3")]
    [Arguments("spdx-3.0.1")]
    public async Task Serialize_WhenSpdxClockChanges_InventoryElementsRemainStable(
        string targetName
    )
    {
        var inventory = CreateInventory();
        var target =
            SbomTarget.Parse(targetName)
            ?? throw new InvalidOperationException($"Unknown test target '{targetName}'.");
        var first = ParseObject(
            SbomDocumentSerializer.Serialize(
                inventory,
                target,
                new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero).AddMilliseconds(100)
            )
        );
        var second = ParseObject(
            SbomDocumentSerializer.Serialize(
                inventory,
                target,
                new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero).AddMilliseconds(900)
            )
        );

        var firstDocumentId = GetDocumentInstanceId(first, targetName);
        var secondDocumentId = GetDocumentInstanceId(second, targetName);
        NormalizeDocumentGenerationMetadata(first, targetName);
        NormalizeDocumentGenerationMetadata(second, targetName);

        await Assert.That(firstDocumentId).IsNotEqualTo(secondDocumentId);
        await Assert.That(JsonNode.DeepEquals(first, second)).IsTrue();
    }

    [Test]
    [Arguments("cyclonedx-1.6", "3e92dddbc30cf7f6a02b80f0942b1a4cfd4fb1c26f1dfc4310afa9d613cafb93")]
    [Arguments("cyclonedx-1.7", "df472ef4aaf593904c479293723a1a5c191d6672715c93b3c0b5c318f3914221")]
    [Arguments("spdx-2.3", "239208b7ac287b3cf5d9a9af23f9d69863971102a5e1587a27a398b43490b89b")]
    [Arguments("spdx-3.0.1", "582c64e809d5b3ef9bd0c4de13a32391b47b0284a3e8d199569fb96f649234b1")]
    public async Task SchemaFixture_WhenRead_MatchesPinnedSourceBytes(
        string targetName,
        string expectedHash
    )
    {
        var schemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "SbomSchemas",
            $"{targetName}.json"
        );
        var actualHash = Convert
            .ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(schemaPath)))
            .ToLowerInvariant();

        await Assert.That(actualHash).IsEqualTo(expectedHash);
    }

    [Test]
    public async Task Serialize_WhenSpdx23HasManagedFiles_DoesNotClaimPackageContainment()
    {
        var target =
            SbomTarget.Parse("spdx-2.3")
            ?? throw new InvalidOperationException("SPDX 2.3 target is required.");
        using var document = JsonDocument.Parse(
            SbomDocumentSerializer.Serialize(CreateInventory(), target, DateTimeOffset.UtcNow)
        );

        var relationships = document.RootElement.GetProperty("relationships").EnumerateArray();
        await Assert
            .That(
                relationships.All(relationship =>
                    !string.Equals(
                        relationship.GetProperty("relationshipType").GetString(),
                        "CONTAINS",
                        StringComparison.Ordinal
                    )
                )
            )
            .IsTrue();
    }

    private static SbomInventory CreateInventory()
    {
        var rootReference = SbomIdentifiers.PackReference("root", "1.2.3");
        var dependencyReference = SbomIdentifiers.PackReference("dependency", "2.0.0");
        var linkReference = SbomIdentifiers.LinkReference("guides");
        var rootFile = CreateFile(rootReference, "docs/readme.md", new string('A', 64));
        var linkFile = CreateFile(linkReference, "docs/setup.md", new string('B', 64));

        return new SbomInventory(
            [
                CreateDependencyPack(dependencyReference),
                CreateRootPack(rootReference, dependencyReference, rootFile),
            ],
            [CreateLink(linkReference, linkFile)]
        );
    }

    private static SbomInventory.Pack CreateDependencyPack(string reference) =>
        new(
            "dependency",
            "2.0.0",
            reference,
            "dependency",
            "oci-source",
            null,
            new SbomInventory.SourceIdentity(
                "oci",
                "https://registry.example.test",
                null,
                "team/packs"
            ),
            null,
            new SbomInventory.OciProvenance(
                "https://registry.example.test",
                "team/packs",
                "v2",
                $"sha256:{new string('c', 64)}",
                $"sha256:{new string('d', 64)}",
                4096
            ),
            [],
            [],
            []
        );

    private static SbomInventory.Pack CreateRootPack(
        string reference,
        string dependencyReference,
        SbomInventory.ManagedFile file
    ) =>
        new(
            "root",
            "1.2.3",
            reference,
            "root",
            "git-source",
            "packs",
            new SbomInventory.SourceIdentity(
                "git",
                "https://example.test/packs.git",
                "v1",
                "packs"
            ),
            new SbomInventory.GitProvenance(
                "https://example.test/packs.git",
                "v1",
                "packs",
                new string('e', 40)
            ),
            null,
            [new SbomInventory.Dependency(dependencyReference)],
            [
                new SbomInventory.ExternalSource(
                    "docs",
                    "documentation",
                    "git:https://example.test/docs@main#/",
                    "main",
                    new string('f', 40)
                ),
            ],
            [file]
        );

    private static SbomInventory.Link CreateLink(
        string reference,
        SbomInventory.ManagedFile file
    ) =>
        new(
            "guides",
            reference,
            "git-source",
            new SbomInventory.SourceIdentity(
                "git",
                "https://example.test/links.git",
                "main",
                "guides"
            ),
            new SbomInventory.GitProvenance(
                "https://example.test/links.git",
                "main",
                "guides",
                new string('1', 40)
            ),
            new string('2', 64),
            [file]
        );

    private static SbomInventory.ManagedFile CreateFile(
        string ownerReference,
        string targetPath,
        string sha256
    ) =>
        new(
            SbomIdentifiers.ManagedFileReference(ownerReference, targetPath),
            ownerReference,
            targetPath,
            sha256,
            targetPath,
            "templates/file.md",
            null,
            null,
            "example-source"
        );

    private static string DescribeErrors(string targetName, EvaluationResults result)
    {
        var errors = new List<string> { targetName };
        AddErrors(result, errors);
        return string.Join(Environment.NewLine, errors);
    }

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidOperationException("SBOM document must be a JSON object.");

    private static string GetDocumentInstanceId(JsonObject document, string targetName)
    {
        if (string.Equals(targetName, "spdx-2.3", StringComparison.Ordinal))
        {
            return document["documentNamespace"]?.GetValue<string>()
                ?? throw new InvalidOperationException("SPDX 2.3 documentNamespace is required.");
        }

        if (document["@graph"] is not JsonArray graph)
        {
            throw new InvalidOperationException("SPDX 3.0.1 @graph is required.");
        }

        return graph
                .OfType<JsonObject>()
                .Single(element =>
                    string.Equals(
                        element["type"]?.GetValue<string>(),
                        "SpdxDocument",
                        StringComparison.Ordinal
                    )
                )["spdxId"]
                ?.GetValue<string>()
            ?? throw new InvalidOperationException("SPDX 3.0.1 document spdxId is required.");
    }

    private static void NormalizeDocumentGenerationMetadata(JsonObject document, string targetName)
    {
        if (string.Equals(targetName, "spdx-2.3", StringComparison.Ordinal))
        {
            if (
                document["creationInfo"] is not JsonObject spdx23CreationInfo
                || document["documentNamespace"] is null
            )
            {
                throw new InvalidOperationException("SPDX 2.3 creation metadata is required.");
            }

            spdx23CreationInfo.Remove("created");
            document["documentNamespace"] = "<document-namespace>";
            return;
        }

        if (document["@graph"] is not JsonArray graph)
        {
            throw new InvalidOperationException("SPDX 3.0.1 @graph is required.");
        }

        var creationInfo = graph
            .OfType<JsonObject>()
            .Single(element =>
                string.Equals(
                    element["type"]?.GetValue<string>(),
                    "CreationInfo",
                    StringComparison.Ordinal
                )
            );
        var spdxDocument = graph
            .OfType<JsonObject>()
            .Single(element =>
                string.Equals(
                    element["type"]?.GetValue<string>(),
                    "SpdxDocument",
                    StringComparison.Ordinal
                )
            );
        creationInfo["@id"] = "<creation-info-id>";
        creationInfo.Remove("created");
        spdxDocument["spdxId"] = "<document-id>";
        foreach (var element in graph.OfType<JsonObject>())
        {
            if (element["creationInfo"] is not null)
            {
                element["creationInfo"] = "<creation-info-id>";
            }
        }
    }

    private static void AddErrors(EvaluationResults result, ICollection<string> errors)
    {
        if (errors.Count >= 20)
        {
            return;
        }

        if (result.Errors is { } nodeErrors)
        {
            foreach (var error in nodeErrors)
            {
                errors.Add($"{result.InstanceLocation}: {error.Key}: {error.Value}");
                if (errors.Count >= 20)
                {
                    return;
                }
            }
        }

        if (result.Details is { } details)
        {
            foreach (var detail in details)
            {
                AddErrors(detail, errors);
            }
        }
    }
}
