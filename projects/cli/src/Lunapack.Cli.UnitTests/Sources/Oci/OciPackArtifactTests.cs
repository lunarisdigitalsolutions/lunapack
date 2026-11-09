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
    public async Task Scenario_ReadManifest_RejectsMultipleLayersAndUnsupportedArtifact()
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
}
