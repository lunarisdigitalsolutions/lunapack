using Lunapack.Cli.Project;

namespace Lunapack.Cli.Audit;

internal sealed record AuditReport(
    IReadOnlyList<ProjectLockFile.ResolvedPack> Packs,
    IReadOnlyList<AuditReport.ExternalSource> ExternalSources,
    IReadOnlyList<AuditReport.ExternalFile> ExternalFiles
)
{
    internal sealed record ExternalSource(
        string PackId,
        string PackVersion,
        string Alias,
        string WorkspaceSourceName,
        string Fingerprint,
        string Ref,
        string ResolvedCommit,
        string Status
    );

    internal sealed record ExternalFile(
        string PackId,
        string PackVersion,
        string Alias,
        string WorkspaceSourceName,
        string Fingerprint,
        string Ref,
        string ResolvedCommit,
        string SourcePath,
        string TargetPath,
        string Sha256,
        string Status
    );
}
