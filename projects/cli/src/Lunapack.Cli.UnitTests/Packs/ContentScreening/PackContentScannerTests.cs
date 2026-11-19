using System.Text;
using Lunapack.Cli.Packs.ContentScreening;

namespace Lunapack.Cli.UnitTests.Packs.ContentScreening;

public sealed class PackContentScannerTests
{
    [Test]
    public async Task Scan_ValidUtf8Text_ReportsNormalizedScalarLocationAndSeverity()
    {
        var contents = Encoding.UTF8.GetBytes("first\r\n😀\u202Etext");

        var finding = PackContentScanner.Scan(@"templates\readme.md", contents).Findings.Single();

        await Assert.That(finding.Path).IsEqualTo("templates/readme.md");
        await Assert.That(finding.Line).IsEqualTo(2);
        await Assert.That(finding.Column).IsEqualTo(2);
        await Assert.That(finding.CodePoint).IsEqualTo("U+202E");
        await Assert.That(finding.Severity).IsEqualTo(PackContentFindingSeverity.HighConfidence);
        await Assert
            .That(PackContentDiagnosticFormatter.Format(finding))
            .IsEqualTo(
                "Hidden Unicode in 'templates/readme.md' at line 2, column 2: U+202E (high-confidence)."
            );
    }

    [Test]
    public async Task Scan_BinaryPayloadWithoutHiddenCharacters_ReturnsNoFindings()
    {
        var findings = PackContentScanner.Scan("binary.bin", [0x00, 0xFF, 0x01, 0x80]).Findings;

        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task Scan_NulOrInvalidUtf8Payload_WithRecoverableControl_ReportsFinding()
    {
        var withNul = PackContentScanner.Scan("opaque.txt", [0x41, 0x00, 0xE2, 0x80, 0xAE]);
        var invalidUtf8 = PackContentScanner.Scan("invalid.txt", [0xC3, 0x28, 0xE2, 0x80, 0xAE]);

        await Assert.That(withNul.Findings.Single().CodePoint).IsEqualTo("U+202E");
        await Assert
            .That(withNul.Findings.Single().Severity)
            .IsEqualTo(PackContentFindingSeverity.HighConfidence);
        await Assert.That(invalidUtf8.Findings.Single().CodePoint).IsEqualTo("U+202E");
        await Assert
            .That(invalidUtf8.Findings.Single().Severity)
            .IsEqualTo(PackContentFindingSeverity.HighConfidence);
    }

    [Test]
    public async Task Scan_BomEncodedUnicode_ReportsHiddenCharacter()
    {
        byte[][] payloads =
        [
            [0xFF, 0xFE, 0x0B, 0x20],
            [0xFE, 0xFF, 0x20, 0x0B],
            [0xFF, 0xFE, 0x00, 0x00, 0x0B, 0x20, 0x00, 0x00],
            [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x20, 0x0B],
        ];

        for (var index = 0; index < payloads.Length; index++)
        {
            var payload = payloads[index];
            var result = PackContentScanner.Scan("unicode.txt", payload);
            var findings = result.Findings;
            await Assert.That(findings).Count().IsEqualTo(1).Because($"payload {index}");
            var finding = findings.Single();
            await Assert.That(finding.CodePoint).IsEqualTo("U+200B").Because($"payload {index}");
            await Assert.That(finding.Line).IsEqualTo(1);
            await Assert.That(finding.Column).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Scan_InvalidBomEncodedUnicode_ReportsEarlierHiddenCharacter()
    {
        var finding = PackContentScanner
            .Scan("unicode.txt", [0xFF, 0xFE, 0x2E, 0x20, 0x00, 0xD8])
            .Findings.Single();

        await Assert.That(finding.CodePoint).IsEqualTo("U+202E");
    }

    [Test]
    public async Task Scan_AllLineEndings_TracksOneBasedLinesAndColumns()
    {
        var contents = Encoding.UTF8.GetBytes("a\r\nb\u200Bc\rd\u2066e\n😀\u00AD");

        var findings = PackContentScanner.Scan("rules.txt", contents).Findings;

        await Assert.That(findings).Count().IsEqualTo(3);
        await Assert.That((findings[0].Line, findings[0].Column)).IsEqualTo((2, 2));
        await Assert.That((findings[1].Line, findings[1].Column)).IsEqualTo((3, 2));
        await Assert.That((findings[2].Line, findings[2].Column)).IsEqualTo((4, 2));
    }

    [Test]
    [Arguments(0x061C)]
    [Arguments(0x200E)]
    [Arguments(0x200F)]
    [Arguments(0x202A)]
    [Arguments(0x202B)]
    [Arguments(0x202C)]
    [Arguments(0x202D)]
    [Arguments(0x202E)]
    [Arguments(0x2066)]
    [Arguments(0x2067)]
    [Arguments(0x2068)]
    [Arguments(0x2069)]
    public async Task Scan_BidiControl_ReportsHighConfidence(int codePoint)
    {
        var finding = PackContentScanner
            .Scan("README.md", Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint)))
            .Findings.Single();

        await Assert.That(finding.Severity).IsEqualTo(PackContentFindingSeverity.HighConfidence);
    }

    [Test]
    public async Task Scan_ZeroWidthSpace_ReportsHighConfidence()
    {
        var finding = PackContentScanner
            .Scan("README.md", Encoding.UTF8.GetBytes("\u200B"))
            .Findings.Single();

        await Assert.That(finding.Severity).IsEqualTo(PackContentFindingSeverity.HighConfidence);
    }

    [Test]
    public async Task Scan_OtherFormatCharacter_ReportsWarning()
    {
        var finding = PackContentScanner
            .Scan("README.md", Encoding.UTF8.GetBytes("\u00AD"))
            .Findings.Single();

        await Assert.That(finding.Severity).IsEqualTo(PackContentFindingSeverity.Warning);
    }

    [Test]
    public async Task Scan_ValidEmojiJoinersAndVariationSelectors_ReturnsNoFindings()
    {
        var contents = Encoding.UTF8.GetBytes("👩‍💻 👨🏽‍👩🏾‍👧🏿‍👦 ✈️");

        var findings = PackContentScanner.Scan("emoji.md", contents).Findings;

        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task Scan_OrphanVariationSelector_ReportsWarning()
    {
        var findings = PackContentScanner
            .Scan("text.txt", Encoding.UTF8.GetBytes("text\uFE0F" + char.ConvertFromUtf32(0xE0100)))
            .Findings;

        await Assert.That(findings).Count().IsEqualTo(2);
        await Assert.That(findings[0].CodePoint).IsEqualTo("U+FE0F");
        await Assert.That(findings[1].CodePoint).IsEqualTo("U+E0100");
        await Assert.That(findings[0].Severity).IsEqualTo(PackContentFindingSeverity.Warning);
        await Assert.That(findings[1].Severity).IsEqualTo(PackContentFindingSeverity.Warning);
    }

    [Test]
    public async Task Scan_ZeroWidthJoinerOutsideEmojiSequence_ReportsWarning()
    {
        var finding = PackContentScanner
            .Scan("text.txt", Encoding.UTF8.GetBytes("a\u200Db"))
            .Findings.Single();

        await Assert.That(finding.CodePoint).IsEqualTo("U+200D");
        await Assert.That(finding.Severity).IsEqualTo(PackContentFindingSeverity.Warning);
    }

    [Test]
    public async Task Format_PathContainsTerminalControls_EscapesVisibleRepresentation()
    {
        var finding = new PackContentFinding(
            "evil'\n\u001B]8;;https://example.test\u0007\u202Ename.txt",
            1,
            1,
            0x202E,
            PackContentFindingSeverity.HighConfidence
        );

        var output = PackContentDiagnosticFormatter.Format(finding);

        await Assert.That(output).Contains("evil\\'\\u000A\\u001B");
        await Assert.That(output).Contains("\\u0007\\u202Ename.txt");
        await Assert.That(output).DoesNotContain("\u001B");
        await Assert.That(output).DoesNotContain("\n");
    }

    [Test]
    public async Task Scan_WhenFindingsExceedLimit_RetainsBlockingEvidenceAndOmittedCount()
    {
        var payload = Encoding.UTF8.GetBytes(
            new string('\u00AD', PackContentScanState.MaximumFindingCount) + "\u202E"
        );

        var result = PackContentScanner.Scan("bounded.txt", payload);

        await Assert
            .That(result.Findings)
            .Count()
            .IsEqualTo(PackContentScanState.MaximumFindingCount);
        await Assert.That(result.OmittedFindingCount).IsEqualTo(1L);
        await Assert.That(result.HasHighConfidenceFinding).IsTrue();
        await Assert
            .That(result.Findings[^1].Severity)
            .IsEqualTo(PackContentFindingSeverity.HighConfidence);
        await Assert
            .That(PackContentScreeningPolicy.Blocks(result.HasHighConfidenceFinding, false))
            .IsTrue();
    }
}
