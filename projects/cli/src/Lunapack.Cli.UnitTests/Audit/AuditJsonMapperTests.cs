using Lunapack.Cli.Application.Diagnostics;
using Lunapack.Cli.Audit;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.ContentScreening;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests.Audit;

public sealed class AuditJsonMapperTests
{
    [Test]
    public async Task Scenario_AuditResult_UsesStableStatusesAndOrdering()
    {
        var report = new AuditReport(
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "second",
                    Version = "1.0.0",
                    PackPath = "second",
                },
                new ProjectLockFile.ResolvedPack
                {
                    Id = "first",
                    Version = "2.0.0",
                    PackPath = "first",
                    ManagedFiles =
                    [
                        new ProjectLockFile.ManagedFile { TargetPath = "z.txt", Sha256 = "z" },
                        new ProjectLockFile.ManagedFile { TargetPath = "a.txt", Sha256 = "a" },
                    ],
                },
            ],
            [CreateExternalSource("alias", "first", "configuration drift")],
            [CreateExternalFile("external", "first", "locally modified")],
            [],
            0
        );
        var links = new LinkAuditReport[]
        {
            new("z-link", "source", null, []),
            new(
                "a-link",
                "source",
                "commit",
                [
                    new LinkFileAuditStatus("z.txt", "conflicting"),
                    new LinkFileAuditStatus("a.txt", "ok"),
                ]
            ),
        };

        var result = AuditJsonMapper.MapResult(report, links);

        await Assert.That(result.Packs[0].Id).IsEqualTo("first");
        await Assert.That(result.Packs[1].Id).IsEqualTo("second");
        await Assert.That(result.Packs[0].ManagedFiles[0].TargetPath).IsEqualTo("a.txt");
        await Assert.That(result.Packs[0].ManagedFiles[1].TargetPath).IsEqualTo("z.txt");
        await Assert.That(result.Links[0].Name).IsEqualTo("a-link");
        await Assert.That(result.Links[1].Name).IsEqualTo("z-link");
        await Assert.That(result.Links[0].Files[0].Status).IsEqualTo("current");
        await Assert.That(result.Links[0].Files[1].Status).IsEqualTo("conflicting-owner");
        await Assert.That(result.ExternalSources[0].Status).IsEqualTo("configuration-drift");
        await Assert.That(result.ExternalFiles[0].Status).IsEqualTo("modified-target");
    }

    [Test]
    public async Task Scenario_NonCurrentStates_ProduceEveryStableFindingAndCount()
    {
        var report = new AuditReport(
            [],
            [
                CreateExternalSource("missing", "first", "missing workspace source"),
                CreateExternalSource("invalid", "first", "invalid workspace source"),
                CreateExternalSource("drift", "first", "configuration drift"),
            ],
            [
                CreateExternalFile("missing-file", "first", "missing target"),
                CreateExternalFile("modified-file", "first", "locally modified"),
            ],
            [],
            0
        );
        var links = new LinkAuditReport[]
        {
            new(
                "link",
                "source",
                null,
                [
                    new LinkFileAuditStatus("missing.txt", "missing"),
                    new LinkFileAuditStatus("modified.txt", "modified"),
                    new LinkFileAuditStatus("conflict.txt", "conflicting"),
                ]
            ),
        };

        var findings = AuditJsonMapper.MapFindings(report, links);
        var summary = CliJsonSummary.FromFindings(findings);

        await Assert
            .That(findings.Select(finding => finding.Code))
            .IsEquivalentTo([
                "audit.external-source.missing-workspace",
                "audit.external-source.invalid-workspace",
                "audit.external-source.configuration-drift",
                "audit.external-file.missing-target",
                "audit.external-file.modified-target",
                "audit.link-file.missing-target",
                "audit.link-file.modified-target",
                "audit.link-file.conflicting-owner",
            ]);
        await Assert.That(summary.TotalFindings).IsEqualTo(8);
        await Assert.That(summary.BySeverity.Info).IsZero();
        await Assert.That(summary.BySeverity.Warning).IsEqualTo(6);
        await Assert.That(summary.BySeverity.Error).IsEqualTo(2);

        var externalFile = findings.Single(finding =>
            string.Equals(
                finding.Code,
                "audit.external-file.missing-target",
                StringComparison.Ordinal
            )
        );
        await Assert.That(externalFile.Subject.Kind).IsEqualTo("external-file");
        await Assert.That(externalFile.Subject.Id).IsEqualTo("missing-file");
        await Assert.That(externalFile.Subject.PackId).IsEqualTo("first");
        await Assert.That(externalFile.Subject.Path).IsEqualTo("target.txt");

        var linkFile = findings.Single(finding =>
            string.Equals(
                finding.Code,
                "audit.link-file.conflicting-owner",
                StringComparison.Ordinal
            )
        );
        await Assert.That(linkFile.Subject.Kind).IsEqualTo("link-file");
        await Assert.That(linkFile.Subject.Id).IsEqualTo("link");
        await Assert.That(linkFile.Subject.PackId).IsNull();
        await Assert.That(linkFile.Subject.Path).IsEqualTo("conflict.txt");
    }

    [Test]
    public async Task Scenario_HiddenUnicodeAndOmission_ProduceStableFindings()
    {
        var report = new AuditReport(
            [],
            [],
            [],
            [
                new PackContentFinding(
                    "docs/readme.md",
                    2,
                    3,
                    0x202E,
                    PackContentFindingSeverity.HighConfidence
                ),
                new PackContentFinding(
                    "docs/notice.md",
                    1,
                    1,
                    0x00AD,
                    PackContentFindingSeverity.Warning
                ),
            ],
            4
        );

        var findings = AuditJsonMapper.MapFindings(report, []);
        var summary = CliJsonSummary.FromFindings(findings);

        await Assert
            .That(findings.Select(finding => finding.Code))
            .IsEquivalentTo([
                "audit.hidden-unicode.high-confidence",
                "audit.hidden-unicode.warning",
                "audit.hidden-unicode.omitted",
            ]);
        await Assert.That(summary.TotalFindings).IsEqualTo(3);
        await Assert.That(summary.BySeverity.Warning).IsEqualTo(2);
        await Assert.That(summary.BySeverity.Error).IsEqualTo(1);
        await Assert
            .That(
                findings
                    .Single(finding =>
                        string.Equals(
                            finding.Code,
                            "audit.hidden-unicode.high-confidence",
                            StringComparison.Ordinal
                        )
                    )
                    .Subject.Path
            )
            .IsEqualTo("docs/readme.md");
        var highConfidence = findings.Single(finding =>
            string.Equals(
                finding.Code,
                "audit.hidden-unicode.high-confidence",
                StringComparison.Ordinal
            )
        );
        await Assert.That(highConfidence.Line).IsEqualTo(2);
        await Assert.That(highConfidence.Column).IsEqualTo(3);
        await Assert.That(highConfidence.CodePoint).IsEqualTo("U+202E");
    }

    private static AuditReport.ExternalSource CreateExternalSource(
        string alias,
        string packId,
        string status
    ) => new(packId, "1.0.0", alias, "workspace", "fingerprint", "main", "commit", status);

    private static AuditReport.ExternalFile CreateExternalFile(
        string alias,
        string packId,
        string status
    ) =>
        new(
            packId,
            "1.0.0",
            alias,
            "workspace",
            "fingerprint",
            "main",
            "commit",
            "source.txt",
            "target.txt",
            "sha256",
            status
        );
}
