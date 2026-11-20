using System.Text;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.Sbom;

internal static class SbomInventoryBuilder
{
    public static SbomInventory Build(
        ProjectLockFile lockFile,
        Func<string, string>? normalizeManagedTarget = null,
        StringComparison pathComparison = StringComparison.Ordinal
    )
    {
        var normalizeTarget = normalizeManagedTarget ?? ProjectPath.Normalize;
        var packs = lockFile
            .Packs.Select(pack => ToPack(pack, normalizeTarget, pathComparison))
            .OrderBy(pack => pack.Reference, StringComparer.Ordinal)
            .ToArray();
        var links = lockFile
            .Links.OrderBy(link => link.Key, StringComparer.Ordinal)
            .Select(link => ToLink(link.Key, link.Value, normalizeTarget, pathComparison))
            .ToArray();

        return new SbomInventory(packs, links);
    }

    private static SbomInventory.Pack ToPack(
        ProjectLockFile.ResolvedPack pack,
        Func<string, string> normalizeTarget,
        StringComparison pathComparison
    )
    {
        var reference = SbomIdentifiers.PackReference(pack.Id, pack.Version);
        var dependencies = pack
            .Packs.Select(dependency => new SbomInventory.Dependency(
                SbomIdentifiers.PackReference(dependency.Id, dependency.Version)
            ))
            .DistinctBy(dependency => dependency.Reference, StringComparer.Ordinal)
            .OrderBy(dependency => dependency.Reference, StringComparer.Ordinal)
            .ToArray();
        var externalSources = pack
            .ExternalSources.OrderBy(source => source.Key, StringComparer.Ordinal)
            .Select(source => ToExternalSource(source.Key, source.Value))
            .ToArray();
        var files = pack
            .ManagedFiles.Select(file =>
                ToManagedFile(
                    reference,
                    normalizeTarget(file.TargetPath),
                    file.Sha256,
                    file.DeclaredTargetPath,
                    file.SourcePath,
                    file.SourceAlias,
                    file.SourceFingerprint,
                    file.SourceName,
                    GetExternalReference(pack.ExternalSources, file.SourceAlias)
                )
            )
            .OrderBy(file => file.TargetPath, StringComparer.Ordinal)
            .ThenBy(file => file.Reference, StringComparer.Ordinal)
            .ToArray();
        EnsureUniqueManagedTargets(pack.Id, files, pathComparison);

        return new SbomInventory.Pack(
            pack.Id,
            pack.Version,
            reference,
            pack.PackPath,
            pack.SourceName,
            pack.SourcePath,
            ToOptionalSourceIdentity(pack.SourceIdentity),
            ToGitProvenance(pack.GitSource),
            ToOciProvenance(pack.OciSource),
            dependencies,
            externalSources,
            files
        );
    }

    private static SbomInventory.Link ToLink(
        string name,
        ProjectLockFile.ResolvedLink link,
        Func<string, string> normalizeTarget,
        StringComparison pathComparison
    )
    {
        var reference = SbomIdentifiers.LinkReference(name);
        var files = link
            .Files.Select(file =>
                ToManagedFile(
                    reference,
                    normalizeTarget(file.TargetPath),
                    file.Sha256,
                    file.DeclaredTargetPath,
                    file.SourcePath,
                    null,
                    null,
                    null
                )
            )
            .OrderBy(file => file.TargetPath, StringComparer.Ordinal)
            .ThenBy(file => file.Reference, StringComparer.Ordinal)
            .ToArray();
        EnsureUniqueManagedTargets(name, files, pathComparison);

        return new SbomInventory.Link(
            name,
            reference,
            link.SourceName,
            ToSourceIdentity(link.SourceIdentity),
            ToGitProvenance(link.GitSource),
            link.DefinitionSha256,
            files
        );
    }

    private static SbomInventory.ManagedFile ToManagedFile(
        string ownerReference,
        string targetPath,
        string sha256,
        string? declaredTargetPath,
        string? sourcePath,
        string? sourceAlias,
        string? sourceFingerprint,
        string? sourceName,
        string? sourceReference = null
    ) =>
        new(
            SbomIdentifiers.ManagedFileReference(ownerReference, targetPath),
            ownerReference,
            targetPath,
            sha256,
            declaredTargetPath,
            sourcePath,
            sourceAlias,
            SanitizeFingerprint(sourceFingerprint, sourceReference),
            sourceName
        );

    private static SbomInventory.ExternalSource ToExternalSource(
        string alias,
        ProjectLockFile.ExternalSourceLock source
    )
    {
        if (!GitRefSafety.IsSafe(source.Ref))
        {
            throw new InvalidDataException(
                $"External source '{alias}' contains an invalid Git reference."
            );
        }

        return new(
            alias,
            source.SourceName,
            SbomSourceUrlSanitizer.SanitizeFingerprint(source.Fingerprint, source.Ref),
            source.Ref,
            source.ResolvedCommit
        );
    }

    private static SbomInventory.SourceIdentity ToSourceIdentity(
        ConfiguredSourceIdentity sourceIdentity
    ) =>
        new(
            sourceIdentity.Type,
            SanitizeUrl(sourceIdentity.Url),
            EnsureSafeOptionalRef(sourceIdentity.Ref),
            sourceIdentity.Path
        );

    private static SbomInventory.SourceIdentity? ToOptionalSourceIdentity(
        ConfiguredSourceIdentity? sourceIdentity
    ) => sourceIdentity is null ? null : ToSourceIdentity(sourceIdentity);

    private static SbomInventory.GitProvenance? ToGitProvenance(GitSourceProvenance? source) =>
        source switch
        {
            null => null,
            { Url: null or "" } => throw new InvalidDataException(
                "Git provenance must include a repository URL."
            ),
            _ => new SbomInventory.GitProvenance(
                SbomSourceUrlSanitizer.Sanitize(source.Url),
                EnsureSafeOptionalRef(source.Ref),
                source.Path,
                source.ResolvedCommit
            ),
        };

    private static SbomInventory.OciProvenance? ToOciProvenance(OciSourceProvenance? source) =>
        source is null
            ? null
            : new SbomInventory.OciProvenance(
                SbomSourceUrlSanitizer.Sanitize(source.RegistryUrl),
                source.Repository,
                source.Tag,
                source.ManifestDigest,
                source.ContentDigest,
                source.ContentSize
            );

    private static string? SanitizeUrl(string? url) =>
        url is null ? null : SbomSourceUrlSanitizer.Sanitize(url);

    private static string? SanitizeFingerprint(string? fingerprint, string? reference) =>
        fingerprint is null
            ? null
            : SbomSourceUrlSanitizer.SanitizeFingerprint(fingerprint, reference);

    private static string? GetExternalReference(
        Dictionary<string, ProjectLockFile.ExternalSourceLock> externalSources,
        string? alias
    ) =>
        alias is not null && externalSources.TryGetValue(alias, out var source) ? source.Ref : null;

    private static string? EnsureSafeOptionalRef(string? reference)
    {
        if (reference is not null && !GitRefSafety.IsSafe(reference))
        {
            throw new InvalidDataException("Git provenance contains an invalid reference.");
        }

        return reference;
    }

    private static void EnsureUniqueManagedTargets(
        string owner,
        IReadOnlyList<SbomInventory.ManagedFile> files,
        StringComparison pathComparison
    )
    {
        var duplicate = files
            .GroupBy(
                file => file.TargetPath.Normalize(NormalizationForm.FormC),
                pathComparison == StringComparison.OrdinalIgnoreCase
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            )
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Owner '{owner}' contains duplicate canonical managed-file target '{duplicate.Key}'."
            );
        }
    }
}
