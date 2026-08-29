namespace Lunapack.Cli.Links;

internal sealed record LinkAuditReport(
    string Name,
    string SourceName,
    string? ResolvedCommit,
    IReadOnlyList<LinkFileAuditStatus> Files
);
