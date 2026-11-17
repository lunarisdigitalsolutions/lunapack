using System.Text.Json;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.UnitTests.Sources.Oci;

public sealed class OciPackArtifactTests
{
    [Test]
    public async Task Scenario_CreateManifest_WritesSupportedSingleZipLayer()
    {
        var pack = CreatePack();
        var (configContent, configDescriptor) = OciPackArtifact.CreateConfig(pack);
        var contentDescriptor = new OciRegistryDescriptor(
            OciPackMediaTypes.ContentZip,
            OciPackArtifact.ComputeDigest("zip"u8),
            3
        );

        var created = OciPackArtifact.CreateManifest(pack, configDescriptor, contentDescriptor);
        var manifest = JsonDocument.Parse(created.Value.Content);
        var root = manifest.RootElement;

        await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(2);
        await Assert
            .That(root.GetProperty("artifactType").GetString())
            .IsEqualTo(OciPackMediaTypes.PackArtifact);
        await Assert
            .That(root.GetProperty("config").GetProperty("size").GetInt64())
            .IsEqualTo(configContent.LongLength);
        await Assert.That(root.GetProperty("layers").GetArrayLength()).IsEqualTo(1);
        await Assert
            .That(root.GetProperty("layers")[0].GetProperty("mediaType").GetString())
            .IsEqualTo(OciPackMediaTypes.ContentZip);
    }

    [Test]
    public async Task Scenario_ReadManifestAndConfig_RequiresConsistentMetadata()
    {
        var pack = CreatePack();
        var (configContent, configDescriptor) = OciPackArtifact.CreateConfig(pack);
        var contentDescriptor = new OciRegistryDescriptor(
            OciPackMediaTypes.ContentZip,
            OciPackArtifact.ComputeDigest("zip"u8),
            3
        );
        var manifest = OciPackArtifact.CreateManifest(pack, configDescriptor, contentDescriptor);

        var parsedManifest = OciPackArtifact.ReadManifest(manifest.Value.Content);
        var parsedConfig = OciPackArtifact.ReadConfig(configContent, parsedManifest.Value!);

        await Assert.That(parsedManifest.IsSuccess).IsTrue();
        await Assert.That(parsedConfig.Value?.Id).IsEqualTo("backend");
        await Assert.That(parsedConfig.Value?.Version).IsEqualTo("1.4.0+build.5");
    }

    [Test]
    public async Task Scenario_ReadManifest_RejectsMultipleLayers()
    {
        var pack = CreatePack();
        var (_, configDescriptor) = OciPackArtifact.CreateConfig(pack);
        var contentDescriptor = new OciRegistryDescriptor(
            OciPackMediaTypes.ContentZip,
            OciPackArtifact.ComputeDigest("zip"u8),
            3
        );
        var created = OciPackArtifact.CreateManifest(pack, configDescriptor, contentDescriptor);
        var document = JsonDocument.Parse(created.Value.Content);
        var manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            document.RootElement.GetRawText()
        )!;
        manifest["layers"] = JsonDocument
            .Parse(
                $"[{document.RootElement.GetProperty("layers")[0].GetRawText()},{document.RootElement.GetProperty("layers")[0].GetRawText()}]"
            )
            .RootElement.Clone();

        var result = OciPackArtifact.ReadManifest(JsonSerializer.SerializeToUtf8Bytes(manifest));

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Scenario_ReadManifest_RejectsUnsupportedArtifactType()
    {
        var manifestBytes = CreateManifestBytes();
        var manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(manifestBytes)!;
        using var unsupportedType = JsonDocument.Parse("\"application/vnd.example.other\"");
        manifest["artifactType"] = unsupportedType.RootElement.Clone();

        var result = OciPackArtifact.ReadManifest(JsonSerializer.SerializeToUtf8Bytes(manifest));

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Scenario_ReadManifest_RejectsMissingRequiredAnnotation()
    {
        var manifestBytes = CreateManifestBytes();
        var manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(manifestBytes)!;
        var annotations = JsonSerializer.Deserialize<Dictionary<string, string>>(
            manifest["annotations"].GetRawText()
        )!;
        annotations.Remove("io.lunapack.pack.id");
        using var changedAnnotations = JsonDocument.Parse(JsonSerializer.Serialize(annotations));
        manifest["annotations"] = changedAnnotations.RootElement.Clone();

        var result = OciPackArtifact.ReadManifest(JsonSerializer.SerializeToUtf8Bytes(manifest));

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Scenario_ReadManifest_EnforcesExactMetadataLimit()
    {
        var manifestBytes = CreateManifestBytes();
        var exactSizeContent = new byte[OciDistributionRegistryClient.MaximumMetadataBytes];
        manifestBytes.CopyTo(exactSizeContent, 0);
        Array.Fill(
            exactSizeContent,
            (byte)' ',
            manifestBytes.Length,
            exactSizeContent.Length - manifestBytes.Length
        );
        var oversizedContent = new byte[exactSizeContent.Length + 1];
        exactSizeContent.CopyTo(oversizedContent, 0);
        oversizedContent[^1] = (byte)' ';

        var exact = OciPackArtifact.ReadManifest(exactSizeContent);
        var oversized = OciPackArtifact.ReadManifest(oversizedContent);

        await Assert.That(exact.IsSuccess).IsTrue().Because(exact.Error ?? string.Empty);
        await Assert.That(oversized.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Scenario_ReadManifest_RejectsOversizedContentDescriptor()
    {
        var manifestBytes = CreateManifestBytes();
        var manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(manifestBytes)!;
        var layers = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(
            manifest["layers"].GetRawText()
        )!;
        using var oversizedSize = JsonDocument.Parse(
            JsonSerializer.Serialize(512L * 1024 * 1024 + 1)
        );
        layers[0]["size"] = oversizedSize.RootElement.Clone();
        using var changedLayers = JsonDocument.Parse(JsonSerializer.Serialize(layers));
        manifest["layers"] = changedLayers.RootElement.Clone();

        var result = OciPackArtifact.ReadManifest(JsonSerializer.SerializeToUtf8Bytes(manifest));

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Scenario_CreateManifest_RejectsInvalidAndOversizedDescriptors()
    {
        var pack = CreatePack();
        var (_, configDescriptor) = OciPackArtifact.CreateConfig(pack);
        var contentDescriptor = new OciRegistryDescriptor(
            OciPackMediaTypes.ContentZip,
            OciPackArtifact.ComputeDigest("zip"u8),
            3
        );
        var invalidDescriptors = new[]
        {
            (configDescriptor with { MediaType = OciPackMediaTypes.ContentZip }, contentDescriptor),
            (configDescriptor with { Digest = "sha256:invalid" }, contentDescriptor),
            (configDescriptor, contentDescriptor with { Size = -1 }),
            (configDescriptor, contentDescriptor with { Size = 512L * 1024 * 1024 + 1 }),
        };

        foreach (var (config, content) in invalidDescriptors)
        {
            var result = OciPackArtifact.CreateManifest(pack, config, content);
            await Assert.That(result.IsSuccess).IsFalse();
        }
    }

    [Test]
    public async Task Scenario_ReadConfig_RejectsInconsistentMetadata()
    {
        var manifest = OciPackArtifact.ReadManifest(CreateManifestBytes());
        var invalidConfigs = new[]
        {
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = "other",
                ["formatVersion"] = 1,
                ["version"] = "1.4.0+build.5",
            },
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = "backend",
                ["formatVersion"] = 2,
                ["version"] = "1.4.0+build.5",
            },
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = "backend",
                ["formatVersion"] = 1,
                ["version"] = "not-a-version",
            },
        };

        foreach (var invalidConfig in invalidConfigs)
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(invalidConfig);
            var metadata = manifest.Value! with
            {
                Config = OciPackArtifact.CreateDescriptor(OciPackMediaTypes.Config, content),
            };
            var result = OciPackArtifact.ReadConfig(content, metadata);

            await Assert.That(result.IsSuccess).IsFalse();
        }
    }

    [Test]
    public async Task Scenario_ReadConfig_RejectsOversizedMetadata()
    {
        var manifest = OciPackArtifact.ReadManifest(CreateManifestBytes());
        var content = new byte[OciDistributionRegistryClient.MaximumMetadataBytes + 1];
        var metadata = manifest.Value! with
        {
            Config = OciPackArtifact.CreateDescriptor(OciPackMediaTypes.Config, content),
        };

        var result = OciPackArtifact.ReadConfig(content, metadata);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Scenario_ReadManifest_RejectsMalformedJson()
    {
        var result = OciPackArtifact.ReadManifest("{invalid"u8.ToArray());

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Scenario_ReadConfig_RejectsDescriptorDigestMismatch()
    {
        var pack = CreatePack();
        var (configContent, configDescriptor) = OciPackArtifact.CreateConfig(pack);
        var contentDescriptor = new OciRegistryDescriptor(
            OciPackMediaTypes.ContentZip,
            OciPackArtifact.ComputeDigest("zip"u8),
            3
        );
        var manifest = OciPackArtifact.CreateManifest(pack, configDescriptor, contentDescriptor);
        var artifact = OciPackArtifact.ReadManifest(manifest.Value.Content);
        var substitutedConfig = configContent.ToArray();
        substitutedConfig[^1] ^= 1;

        var result = OciPackArtifact.ReadConfig(substitutedConfig, artifact.Value!);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    private static PackManifest CreatePack() =>
        new()
        {
            Id = "backend",
            Version = "1.4.0+build.5",
            Name = "Backend",
            Description = "A backend pack",
        };

    private static byte[] CreateManifestBytes()
    {
        var pack = CreatePack();
        var (_, configDescriptor) = OciPackArtifact.CreateConfig(pack);
        var contentDescriptor = new OciRegistryDescriptor(
            OciPackMediaTypes.ContentZip,
            OciPackArtifact.ComputeDigest("zip"u8),
            3
        );
        return OciPackArtifact
            .CreateManifest(pack, configDescriptor, contentDescriptor)
            .Value.Content;
    }
}
