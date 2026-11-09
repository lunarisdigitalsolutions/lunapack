using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.Sources.Oci;

internal static class OciPackArtifact
{
    private const int FormatVersion = 1;
    private const int MaximumMetadataBytes = 1024 * 1024;
    private const long MaximumContentBytes = 512L * 1024 * 1024;
    private static readonly SearchValues<char> _hexCharacters = SearchValues.Create(
        "0123456789abcdef"
    );

    internal sealed record OciPackConfig
    {
        public required string Id { get; init; }

        public required int FormatVersion { get; init; }

        public required string Version { get; init; }
    }

    internal sealed record OciArtifactDescriptor
    {
        public required string Digest { get; init; }

        public required string MediaType { get; init; }

        public required long Size { get; init; }
    }

    internal sealed record OciPackImageManifest
    {
        public string? ArtifactType { get; init; }

        public required Dictionary<string, string> Annotations { get; init; }

        public required OciArtifactDescriptor Config { get; init; }

        public List<OciArtifactDescriptor> Layers { get; init; } = [];

        public string? MediaType { get; init; }

        public int SchemaVersion { get; init; }
    }

    internal sealed record OciPackArtifactMetadata(
        OciRegistryDescriptor Config,
        OciRegistryDescriptor Content,
        IReadOnlyDictionary<string, string> Annotations
    );

    public static (byte[] Content, OciRegistryDescriptor Descriptor) CreateConfig(PackManifest pack)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(
            new OciPackConfig
            {
                Id = pack.Id,
                Version = pack.Version,
                FormatVersion = FormatVersion,
            },
            LunapackJsonContext.Default.OciPackConfig
        );
        return (content, CreateDescriptor(OciPackMediaTypes.Config, content));
    }

    public static ManifestOperationResult<(
        byte[] Content,
        OciRegistryDescriptor Descriptor
    )> CreateManifest(
        PackManifest pack,
        OciRegistryDescriptor configDescriptor,
        OciRegistryDescriptor contentDescriptor,
        string? source = null,
        string? revision = null
    )
    {
        if (
            !string.Equals(
                configDescriptor.MediaType,
                OciPackMediaTypes.Config,
                StringComparison.Ordinal
            )
            || !string.Equals(
                contentDescriptor.MediaType,
                OciPackMediaTypes.ContentZip,
                StringComparison.Ordinal
            )
            || !IsValidDigest(configDescriptor.Digest)
            || !IsValidDigest(contentDescriptor.Digest)
            || configDescriptor.Size is < 0 or > MaximumMetadataBytes
            || contentDescriptor.Size is < 0 or > MaximumContentBytes
        )
        {
            return ManifestOperationResult<(byte[], OciRegistryDescriptor)>.Failure(
                "OCI pack artifact descriptors are invalid or exceed supported limits."
            );
        }

        var annotations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["io.lunapack.pack.id"] = pack.Id,
            ["io.lunapack.pack.format-version"] = FormatVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            ["org.opencontainers.image.version"] = pack.Version,
        };
        if (!string.IsNullOrWhiteSpace(pack.Name))
        {
            annotations["org.opencontainers.image.title"] = pack.Name;
        }

        if (!string.IsNullOrWhiteSpace(pack.Description))
        {
            annotations["org.opencontainers.image.description"] = pack.Description;
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            annotations["org.opencontainers.image.source"] = source;
        }

        if (!string.IsNullOrWhiteSpace(revision))
        {
            annotations["org.opencontainers.image.revision"] = revision;
        }

        var manifest = new OciPackImageManifest
        {
            SchemaVersion = 2,
            MediaType = OciPackMediaTypes.Manifest,
            ArtifactType = OciPackMediaTypes.PackArtifact,
            Config = ToArtifactDescriptor(configDescriptor),
            Layers = [ToArtifactDescriptor(contentDescriptor)],
            Annotations = annotations,
        };
        var content = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            LunapackJsonContext.Default.OciPackImageManifest
        );
        if (content.Length > MaximumMetadataBytes)
        {
            return ManifestOperationResult<(byte[], OciRegistryDescriptor)>.Failure(
                "OCI pack manifest metadata exceeds the configured limit."
            );
        }

        return ManifestOperationResult<(byte[], OciRegistryDescriptor)>.Success(
            (content, CreateDescriptor(OciPackMediaTypes.Manifest, content))
        );
    }

    public static ManifestOperationResult<OciPackArtifactMetadata> ReadManifest(byte[] content)
    {
        if (content.Length > MaximumMetadataBytes)
        {
            return ManifestOperationResult<OciPackArtifactMetadata>.Failure(
                "OCI pack manifest metadata exceeds the configured limit."
            );
        }

        ManifestOperationResult<OciPackImageManifest> parsedManifest;
        try
        {
            var manifest = JsonSerializer.Deserialize(
                content,
                LunapackJsonContext.Default.OciPackImageManifest
            );
            parsedManifest = manifest is null
                ? ManifestOperationResult<OciPackImageManifest>.Failure(
                    "OCI pack manifest metadata is invalid."
                )
                : ManifestOperationResult<OciPackImageManifest>.Success(manifest);
        }
        catch (JsonException)
        {
            return ManifestOperationResult<OciPackArtifactMetadata>.Failure(
                "OCI pack manifest metadata is invalid."
            );
        }

        return parsedManifest.Value is { } parsed
            ? ValidateManifest(parsed)
            : ManifestOperationResult<OciPackArtifactMetadata>.Failure(
                parsedManifest.Error ?? "OCI pack manifest metadata is invalid."
            );
    }

    public static ManifestOperationResult<OciPackConfig> ReadConfig(
        byte[] content,
        OciPackArtifactMetadata artifact
    )
    {
        if (content.Length > MaximumMetadataBytes || !MatchesDescriptor(content, artifact.Config))
        {
            return ManifestOperationResult<OciPackConfig>.Failure(
                "OCI pack configuration does not match its descriptor."
            );
        }

        try
        {
            var config = JsonSerializer.Deserialize(
                content,
                LunapackJsonContext.Default.OciPackConfig
            );
            if (
                config is null
                || config.FormatVersion != FormatVersion
                || string.IsNullOrWhiteSpace(config.Id)
                || string.IsNullOrWhiteSpace(config.Version)
                || !NuGet.Versioning.NuGetVersion.TryParse(config.Version, out _)
                || !string.Equals(
                    artifact.Annotations["io.lunapack.pack.id"],
                    config.Id,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    artifact.Annotations["org.opencontainers.image.version"],
                    config.Version,
                    StringComparison.Ordinal
                )
            )
            {
                return ManifestOperationResult<OciPackConfig>.Failure(
                    "OCI pack configuration metadata is inconsistent."
                );
            }

            return ManifestOperationResult<OciPackConfig>.Success(config);
        }
        catch (JsonException)
        {
            return ManifestOperationResult<OciPackConfig>.Failure(
                "OCI pack configuration metadata is invalid."
            );
        }
    }

    public static ManifestOperationResult<bool> ValidateReleaseIdentity(
        OciPackConfig config,
        string repositoryPackId,
        string tag
    )
    {
        var version = OciRepositoryTagMapper.ReadVersion(tag);
        return
            version.Value is { } expectedVersion
            && string.Equals(config.Id, repositoryPackId, StringComparison.Ordinal)
            && string.Equals(config.Version, expectedVersion, StringComparison.Ordinal)
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(
                "OCI pack metadata does not match the repository ID and requested version tag."
            );
    }

    public static OciRegistryDescriptor CreateDescriptor(string mediaType, byte[] content) =>
        new(mediaType, ComputeDigest(content), content.LongLength);

    public static bool IsLunaPackArtifact(ReadOnlyMemory<byte> content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("artifactType", out var artifactType)
                && artifactType.ValueKind == JsonValueKind.String
                && string.Equals(
                    artifactType.GetString(),
                    OciPackMediaTypes.PackArtifact,
                    StringComparison.Ordinal
                );
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string ComputeDigest(ReadOnlySpan<byte> content) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}";

    public static bool MatchesDescriptor(byte[] content, OciRegistryDescriptor descriptor) =>
        descriptor.Size == content.LongLength
        && string.Equals(ComputeDigest(content), descriptor.Digest, StringComparison.Ordinal);

    private static OciArtifactDescriptor ToArtifactDescriptor(OciRegistryDescriptor descriptor) =>
        new()
        {
            MediaType = descriptor.MediaType,
            Digest = descriptor.Digest,
            Size = descriptor.Size,
        };

    private static OciRegistryDescriptor ToRegistryDescriptor(OciArtifactDescriptor descriptor) =>
        new(descriptor.MediaType, descriptor.Digest, descriptor.Size);

    private static bool IsValidDigest(string? value) =>
        value is { Length: 71 }
        && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value.AsSpan(7).IndexOfAnyExcept(_hexCharacters) < 0;

    private static ManifestOperationResult<OciPackArtifactMetadata> ValidateManifest(
        OciPackImageManifest manifest
    )
    {
        if (
            manifest.SchemaVersion != 2
            || !string.Equals(
                manifest.MediaType,
                OciPackMediaTypes.Manifest,
                StringComparison.Ordinal
            )
            || !string.Equals(
                manifest.ArtifactType,
                OciPackMediaTypes.PackArtifact,
                StringComparison.Ordinal
            )
            || manifest.Annotations is null
            || manifest.Config is null
            || manifest.Layers is null
            || manifest.Layers.Count != 1
            || manifest.Layers[0] is null
        )
        {
            return ManifestOperationResult<OciPackArtifactMetadata>.Failure(
                "OCI manifest is not a supported LunaPack v1 artifact."
            );
        }

        var config = ToRegistryDescriptor(manifest.Config);
        var layer = ToRegistryDescriptor(manifest.Layers[0]);
        if (
            !string.Equals(config.MediaType, OciPackMediaTypes.Config, StringComparison.Ordinal)
            || !string.Equals(
                layer.MediaType,
                OciPackMediaTypes.ContentZip,
                StringComparison.Ordinal
            )
            || !IsValidDigest(config.Digest)
            || !IsValidDigest(layer.Digest)
            || config.Size is < 0 or > MaximumMetadataBytes
            || layer.Size is < 0 or > MaximumContentBytes
        )
        {
            return ManifestOperationResult<OciPackArtifactMetadata>.Failure(
                "OCI manifest contains an unsupported or invalid LunaPack descriptor."
            );
        }

        if (!HasRequiredAnnotations(manifest.Annotations))
        {
            return ManifestOperationResult<OciPackArtifactMetadata>.Failure(
                "OCI manifest is missing required LunaPack annotations."
            );
        }

        var formatVersion = FormatVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );
        if (
            !string.Equals(
                manifest.Annotations["io.lunapack.pack.format-version"],
                formatVersion,
                StringComparison.Ordinal
            )
        )
        {
            return ManifestOperationResult<OciPackArtifactMetadata>.Failure(
                "OCI pack format version is not supported."
            );
        }

        return ManifestOperationResult<OciPackArtifactMetadata>.Success(
            new OciPackArtifactMetadata(config, layer, manifest.Annotations)
        );
    }

    private static bool HasRequiredAnnotations(Dictionary<string, string> annotations) =>
        annotations.ContainsKey("io.lunapack.pack.id")
        && annotations.ContainsKey("io.lunapack.pack.format-version")
        && annotations.ContainsKey("org.opencontainers.image.version");
}
