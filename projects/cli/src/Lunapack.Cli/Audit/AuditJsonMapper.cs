using Lunapack.Cli.Application.Diagnostics;
using Lunapack.Cli.Links;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Audit;

internal static class AuditJsonMapper
{
    public static CliJsonAuditResult MapResult(
        AuditReport report,
        IReadOnlyList<LinkAuditReport> links
    ) =>
        new(
            [
                .. report
                    .Packs.OrderBy(pack => pack.Id, StringComparer.Ordinal)
                    .ThenBy(pack => pack.Version, StringComparer.Ordinal)
                    .Select(MapPack),
            ],
            [.. links.OrderBy(link => link.Name, StringComparer.Ordinal).Select(MapLink)],
            [
                .. report
                    .ExternalSources.OrderBy(source => source.PackId, StringComparer.Ordinal)
                    .ThenBy(source => source.PackVersion, StringComparer.Ordinal)
                    .ThenBy(source => source.Alias, StringComparer.Ordinal)
                    .Select(MapExternalSource),
            ],
            [
                .. report
                    .ExternalFiles.OrderBy(file => file.PackId, StringComparer.Ordinal)
                    .ThenBy(file => file.PackVersion, StringComparer.Ordinal)
                    .ThenBy(file => file.Alias, StringComparer.Ordinal)
                    .ThenBy(file => file.TargetPath, StringComparer.Ordinal)
                    .Select(MapExternalFile),
            ]
        );

    public static IReadOnlyList<CliJsonFinding> MapFindings(
        AuditReport report,
        IReadOnlyList<LinkAuditReport> links
    )
    {
        var findings = new List<CliJsonFinding>();
        foreach (var source in report.ExternalSources)
        {
            AddFinding(
                findings,
                MapExternalSourceStatus(source.Status),
                new CliJsonFinding.SubjectInfo("external-source", source.Alias, source.PackId),
                $"External source '{source.Alias}' for pack '{source.PackId}@{source.PackVersion}'"
            );
        }

        foreach (var file in report.ExternalFiles)
        {
            AddFinding(
                findings,
                MapExternalFileStatus(file.Status),
                new CliJsonFinding.SubjectInfo(
                    "external-file",
                    file.Alias,
                    file.PackId,
                    file.TargetPath
                ),
                $"External file '{file.TargetPath}' for pack '{file.PackId}@{file.PackVersion}'"
            );
        }

        foreach (var link in links)
        {
            foreach (var file in link.Files)
            {
                AddFinding(
                    findings,
                    MapLinkStatus(file.Status),
                    new CliJsonFinding.SubjectInfo("link-file", link.Name, Path: file.TargetPath),
                    $"Link '{link.Name}' target '{file.TargetPath}'"
                );
            }
        }

        return
        [
            .. findings
                .OrderBy(finding => finding.Subject.PackId ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(finding => finding.Subject.Id, StringComparer.Ordinal)
                .ThenBy(finding => finding.Subject.Path ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(finding => finding.Code, StringComparer.Ordinal),
        ];
    }

    private static CliJsonAuditResult.Pack MapPack(ProjectLockFile.ResolvedPack pack) =>
        new(
            pack.Id,
            pack.Version,
            pack.SourcePath,
            pack.PackPath,
            [
                .. pack
                    .Packs.OrderBy(dependency => dependency.Id, StringComparer.Ordinal)
                    .ThenBy(dependency => dependency.Version, StringComparer.Ordinal)
                    .Select(dependency => new CliJsonAuditResult.Dependency(
                        dependency.Id,
                        dependency.Version
                    )),
            ],
            [
                .. pack
                    .ManagedFiles.OrderBy(file => file.TargetPath, StringComparer.Ordinal)
                    .Select(file => new CliJsonAuditResult.PackFile(file.TargetPath, file.Sha256)),
            ]
        );

    private static CliJsonAuditResult.Link MapLink(LinkAuditReport link) =>
        new(
            link.Name,
            link.SourceName,
            link.ResolvedCommit,
            [
                .. link
                    .Files.OrderBy(file => file.TargetPath, StringComparer.Ordinal)
                    .Select(file => new CliJsonAuditResult.LinkFile(
                        file.TargetPath,
                        MapLinkStatus(file.Status).Value
                    )),
            ]
        );

    private static CliJsonAuditResult.ExternalSource MapExternalSource(
        AuditReport.ExternalSource source
    ) =>
        new(
            source.PackId,
            source.PackVersion,
            source.Alias,
            source.WorkspaceSourceName,
            source.Fingerprint,
            source.Ref,
            source.ResolvedCommit,
            MapExternalSourceStatus(source.Status).Value
        );

    private static CliJsonAuditResult.ExternalFile MapExternalFile(AuditReport.ExternalFile file) =>
        new(
            file.PackId,
            file.PackVersion,
            file.Alias,
            file.WorkspaceSourceName,
            file.Fingerprint,
            file.Ref,
            file.ResolvedCommit,
            MapExternalFileStatus(file.Status).Value,
            file.SourcePath,
            file.TargetPath,
            file.Sha256
        );

    private static void AddFinding(
        List<CliJsonFinding> findings,
        MappedStatus status,
        CliJsonFinding.SubjectInfo subject,
        string messagePrefix
    )
    {
        if (status is not { Code: { } code, Severity: { } severity })
        {
            return;
        }

        findings.Add(
            new CliJsonFinding(code, severity, subject, $"{messagePrefix} is {status.Description}.")
        );
    }

    private static MappedStatus MapExternalSourceStatus(string status) =>
        status switch
        {
            "current" => new("current", null, null, "current"),
            "missing workspace source" => new(
                "missing-workspace-source",
                "audit.external-source.missing-workspace",
                "warning",
                "missing from the workspace"
            ),
            "invalid workspace source" => new(
                "invalid-workspace-source",
                "audit.external-source.invalid-workspace",
                "error",
                "configured with invalid workspace identity"
            ),
            "configuration drift" => new(
                "configuration-drift",
                "audit.external-source.configuration-drift",
                "warning",
                "different from its locked configuration"
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported external-source audit status '{status}'."
            ),
        };

    private static MappedStatus MapExternalFileStatus(string status) =>
        status switch
        {
            "current" => new("current", null, null, "current"),
            "missing target" => new(
                "missing-target",
                "audit.external-file.missing-target",
                "warning",
                "missing"
            ),
            "locally modified" => new(
                "modified-target",
                "audit.external-file.modified-target",
                "warning",
                "modified"
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported external-file audit status '{status}'."
            ),
        };

    private static MappedStatus MapLinkStatus(string status) =>
        status switch
        {
            "ok" => new("current", null, null, "current"),
            "missing" => new(
                "missing-target",
                "audit.link-file.missing-target",
                "warning",
                "missing"
            ),
            "modified" => new(
                "modified-target",
                "audit.link-file.modified-target",
                "warning",
                "modified"
            ),
            "conflicting" => new(
                "conflicting-owner",
                "audit.link-file.conflicting-owner",
                "error",
                "owned by a conflicting root"
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported link-file audit status '{status}'."
            ),
        };

    private sealed record MappedStatus(
        string Value,
        string? Code,
        string? Severity,
        string Description
    );
}
