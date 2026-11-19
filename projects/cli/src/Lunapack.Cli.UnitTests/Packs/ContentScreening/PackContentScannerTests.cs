using System.Text;
using Lunapack.Cli.Packs.ContentScreening;

namespace Lunapack.Cli.UnitTests.Packs.ContentScreening;

public sealed class PackContentScannerTests
{
    [Test]
    public async Task Scan_ValidUtf8Text_ReportsNormalizedScalarLocationAndSeverity()
    {
        var contents = Encoding.UTF8.GetBytes("first\r\n😀\u202Etext");

        var finding = PackContentScanner.Scan(@"templates\readme.md", contents).Single();

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
    public async Task Scan_BinaryOrInvalidUtf8Payloads_ReturnsNoFindings()
    {
        var binary = PackContentScanner.Scan("binary.bin", [0x20, 0x00, 0xE2, 0x80, 0xAE]);
        var invalid = PackContentScanner.Scan("invalid.txt", [0xC3, 0x28, 0xE2, 0x80, 0xAE]);

        await Assert.That(binary).IsEmpty();
        await Assert.That(invalid).IsEmpty();
    }

    [Test]
    public async Task Scan_AllLineEndings_TracksOneBasedLinesAndColumns()
    {
        var contents = Encoding.UTF8.GetBytes("a\r\nb\u200Bc\rd\u2066e\n😀\u00AD");

        var findings = PackContentScanner.Scan("rules.txt", contents);

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
            .Single();

        await Assert.That(finding.Severity).IsEqualTo(PackContentFindingSeverity.HighConfidence);
    }

    [Test]
    public async Task Scan_ZeroWidthSpace_ReportsHighConfidence()
    {
        var finding = PackContentScanner
            .Scan("README.md", Encoding.UTF8.GetBytes("\u200B"))
            .Single();

        await Assert.That(finding.Severity).IsEqualTo(PackContentFindingSeverity.HighConfidence);
    }

    [Test]
    public async Task Scan_OtherFormatCharacter_ReportsWarning()
    {
        var finding = PackContentScanner
            .Scan("README.md", Encoding.UTF8.GetBytes("\u00AD"))
            .Single();

        await Assert.That(finding.Severity).IsEqualTo(PackContentFindingSeverity.Warning);
    }

    [Test]
    public async Task Scan_ValidEmojiJoinersAndVariationSelectors_ReturnsNoFindings()
    {
        var contents = Encoding.UTF8.GetBytes("👩‍💻 👨🏽‍👩🏾‍👧🏿‍👦 ✈️");

        var findings = PackContentScanner.Scan("emoji.md", contents);

        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task Scan_ZeroWidthJoinerOutsideEmojiSequence_ReportsWarning()
    {
        var finding = PackContentScanner
            .Scan("text.txt", Encoding.UTF8.GetBytes("a\u200Db"))
            .Single();

        await Assert.That(finding.CodePoint).IsEqualTo("U+200D");
        await Assert.That(finding.Severity).IsEqualTo(PackContentFindingSeverity.Warning);
    }
}
