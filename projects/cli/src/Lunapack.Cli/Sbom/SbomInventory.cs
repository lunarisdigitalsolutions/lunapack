namespace Lunapack.Cli.Sbom;

internal sealed record SbomInventory(
    IReadOnlyList<SbomInventory.Pack> Packs,
    IReadOnlyList<SbomInventory.Link> Links
)
{
    internal sealed record Dependency(string Reference);

    internal sealed record ExternalSource(
        string Alias,
        string SourceName,
        string Fingerprint,
        string Ref,
        string ResolvedCommit
    );

    internal sealed record GitProvenance(
        string Url,
        string? Ref,
        string? Path,
        string ResolvedCommit
    );

    internal sealed record Link(
        string Name,
        string Reference,
        string SourceName,
        SourceIdentity SourceIdentity,
        GitProvenance? GitSource,
        string DefinitionSha256,
        IReadOnlyList<ManagedFile> Files
    );

    internal sealed record ManagedFile(
        string Reference,
        string OwnerReference,
        string TargetPath,
        string Sha256,
        string? DeclaredTargetPath,
        string? SourcePath,
        string? SourceAlias,
        string? SourceFingerprint,
        string? SourceName
    );

    internal sealed record OciProvenance(
        string RegistryUrl,
        string Repository,
        string Tag,
        string ManifestDigest,
        string ContentDigest,
        long ContentSize
    );

    internal sealed record Pack(
        string Id,
        string Version,
        string Reference,
        string PackPath,
        string? SourceName,
        string? SourcePath,
        SourceIdentity? SourceIdentity,
        GitProvenance? GitSource,
        OciProvenance? OciSource,
        IReadOnlyList<Dependency> Dependencies,
        IReadOnlyList<ExternalSource> ExternalSources,
        IReadOnlyList<ManagedFile> Files
    );

    internal sealed record SourceIdentity(string Type, string? Url, string? Ref, string? Path);
}
