namespace Lunapack.Cli.Sources.Oci;

internal sealed record OciSourceProvenance
{
    public required long ContentSize { get; set; }

    public required string ContentDigest { get; set; }

    public required string ManifestDigest { get; set; }

    public required string RegistryUrl { get; set; }

    public required string Repository { get; set; }

    public required string Tag { get; set; }
}
