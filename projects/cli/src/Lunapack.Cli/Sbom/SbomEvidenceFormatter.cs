namespace Lunapack.Cli.Sbom;

internal static class SbomEvidenceFormatter
{
    public static IReadOnlyList<KeyValuePair<string, string>> ForPack(SbomInventory.Pack pack)
    {
        var properties = new List<KeyValuePair<string, string>>();
        Add(properties, "pack/id", pack.Id);
        Add(properties, "pack/version", pack.Version);
        Add(properties, "pack/path", pack.PackPath);
        Add(properties, "source/name", pack.SourceName);
        Add(properties, "source/path", pack.SourcePath);
        AddSourceIdentity(properties, pack.SourceIdentity);
        AddGitSource(properties, pack.GitSource);
        AddOciSource(properties, pack.OciSource);
        foreach (var source in pack.ExternalSources)
        {
            var prefix = $"external-source/{source.Alias}/";
            Add(properties, $"{prefix}name", source.SourceName);
            Add(properties, $"{prefix}fingerprint", source.Fingerprint);
            Add(properties, $"{prefix}ref", source.Ref);
            Add(properties, $"{prefix}resolved-commit", source.ResolvedCommit);
        }

        return Order(properties);
    }

    public static IReadOnlyList<KeyValuePair<string, string>> ForLink(SbomInventory.Link link)
    {
        var properties = new List<KeyValuePair<string, string>>();
        Add(properties, "link/name", link.Name);
        Add(properties, "link/definition-sha256", link.DefinitionSha256);
        Add(properties, "source/name", link.SourceName);
        AddSourceIdentity(properties, link.SourceIdentity);
        AddGitSource(properties, link.GitSource);
        return Order(properties);
    }

    public static IReadOnlyList<KeyValuePair<string, string>> ForFile(
        SbomInventory.ManagedFile file
    )
    {
        var properties = new List<KeyValuePair<string, string>>();
        Add(properties, "managed-file/owner", file.OwnerReference);
        Add(properties, "managed-file/target", file.TargetPath);
        Add(properties, "managed-file/sha256", file.Sha256);
        Add(properties, "managed-file/declared-target", file.DeclaredTargetPath);
        Add(properties, "managed-file/source-path", file.SourcePath);
        Add(properties, "managed-file/source-alias", file.SourceAlias);
        Add(properties, "managed-file/source-fingerprint", file.SourceFingerprint);
        Add(properties, "managed-file/source-name", file.SourceName);
        return Order(properties);
    }

    public static string? ForSpdx(IReadOnlyList<KeyValuePair<string, string>> properties) =>
        properties.Count == 0
            ? null
            : string.Join(
                '\n',
                properties.Select(property => $"{property.Key}={EscapeLineBreaks(property.Value)}")
            );

    private static void AddSourceIdentity(
        ICollection<KeyValuePair<string, string>> properties,
        SbomInventory.SourceIdentity? identity
    )
    {
        if (identity is null)
        {
            return;
        }

        Add(properties, "source/identity/type", identity.Type);
        Add(properties, "source/identity/url", identity.Url);
        Add(properties, "source/identity/ref", identity.Ref);
        Add(properties, "source/identity/path", identity.Path);
    }

    private static void AddGitSource(
        ICollection<KeyValuePair<string, string>> properties,
        SbomInventory.GitProvenance? source
    )
    {
        if (source is null)
        {
            return;
        }

        Add(properties, "git/url", source.Url);
        Add(properties, "git/ref", source.Ref);
        Add(properties, "git/path", source.Path);
        Add(properties, "git/resolved-commit", source.ResolvedCommit);
    }

    private static void AddOciSource(
        ICollection<KeyValuePair<string, string>> properties,
        SbomInventory.OciProvenance? source
    )
    {
        if (source is null)
        {
            return;
        }

        Add(properties, "oci/registry", source.RegistryUrl);
        Add(properties, "oci/repository", source.Repository);
        Add(properties, "oci/tag", source.Tag);
        Add(properties, "oci/manifest-digest", source.ManifestDigest);
        Add(properties, "oci/content-digest", source.ContentDigest);
        Add(
            properties,
            "oci/content-size",
            source.ContentSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
    }

    private static void Add(
        ICollection<KeyValuePair<string, string>> properties,
        string name,
        string? value
    )
    {
        if (value is not null)
        {
            properties.Add(new KeyValuePair<string, string>(name, value));
        }
    }

    private static KeyValuePair<string, string>[] Order(
        IEnumerable<KeyValuePair<string, string>> properties
    ) => [.. properties.OrderBy(property => property.Key, StringComparer.Ordinal)];

    private static string EscapeLineBreaks(string value) =>
        value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
}
