using System.Text.Json.Serialization;

namespace Lunapack.Cli.Application.Diagnostics;

internal sealed record CliJsonAuditResult(
    [property: JsonPropertyName("packs")] IReadOnlyList<CliJsonAuditResult.Pack> Packs,
    [property: JsonPropertyName("links")] IReadOnlyList<CliJsonAuditResult.Link> Links,
    [property: JsonPropertyName("externalSources")]
        IReadOnlyList<CliJsonAuditResult.ExternalSource> ExternalSources,
    [property: JsonPropertyName("externalFiles")]
        IReadOnlyList<CliJsonAuditResult.ExternalFile> ExternalFiles
)
{
    internal sealed record Pack(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("sourcePath")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            string? SourcePath,
        [property: JsonPropertyName("packPath")] string PackPath,
        [property: JsonPropertyName("dependencies")] IReadOnlyList<Dependency> Dependencies,
        [property: JsonPropertyName("managedFiles")] IReadOnlyList<PackFile> ManagedFiles
    );

    internal sealed record Dependency(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("version")] string Version
    );

    internal sealed record PackFile(
        [property: JsonPropertyName("targetPath")] string TargetPath,
        [property: JsonPropertyName("sha256")] string Sha256
    );

    internal sealed record Link(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("sourceName")] string SourceName,
        [property: JsonPropertyName("resolvedCommit")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            string? ResolvedCommit,
        [property: JsonPropertyName("files")] IReadOnlyList<LinkFile> Files
    );

    internal sealed record LinkFile(
        [property: JsonPropertyName("targetPath")] string TargetPath,
        [property: JsonPropertyName("status")] string Status
    );

    internal sealed record ExternalSource(
        [property: JsonPropertyName("packId")] string PackId,
        [property: JsonPropertyName("packVersion")] string PackVersion,
        [property: JsonPropertyName("alias")] string Alias,
        [property: JsonPropertyName("workspaceSourceName")] string WorkspaceSourceName,
        [property: JsonPropertyName("fingerprint")] string Fingerprint,
        [property: JsonPropertyName("ref")] string Ref,
        [property: JsonPropertyName("resolvedCommit")] string ResolvedCommit,
        [property: JsonPropertyName("status")] string Status
    );

    internal sealed record ExternalFile(
        [property: JsonPropertyName("packId")] string PackId,
        [property: JsonPropertyName("packVersion")] string PackVersion,
        [property: JsonPropertyName("alias")] string Alias,
        [property: JsonPropertyName("workspaceSourceName")] string WorkspaceSourceName,
        [property: JsonPropertyName("fingerprint")] string Fingerprint,
        [property: JsonPropertyName("ref")] string Ref,
        [property: JsonPropertyName("resolvedCommit")] string ResolvedCommit,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("sourcePath")] string SourcePath,
        [property: JsonPropertyName("targetPath")] string TargetPath,
        [property: JsonPropertyName("sha256")] string Sha256
    );
}
