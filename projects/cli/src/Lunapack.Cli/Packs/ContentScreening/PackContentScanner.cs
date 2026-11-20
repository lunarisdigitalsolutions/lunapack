using System.Buffers;
using System.Text;
using Lunapack.Cli.Application.Paths;

namespace Lunapack.Cli.Packs.ContentScreening;

internal static class PackContentScanner
{
    private static readonly byte[] _utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] _utf16LittleEndianBom = [0xFF, 0xFE];
    private static readonly byte[] _utf16BigEndianBom = [0xFE, 0xFF];
    private static readonly byte[] _utf32LittleEndianBom = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] _utf32BigEndianBom = [0x00, 0x00, 0xFE, 0xFF];

    public static PackContentScanResult Scan(string path, byte[] contents)
    {
        var normalizedPath = ProjectPath.Normalize(path);
        var findings = new List<PackContentFinding>();
        var uniqueFindings = new HashSet<PackContentFinding>();
        var omittedFindingCount = 0L;
        var hasHighConfidenceFinding = false;
        var hasRetainedHighConfidenceFinding = false;
        foreach (var candidate in DecodeCandidates(contents))
        {
            var state = new PackContentScanState(normalizedPath);
            foreach (var rune in candidate)
            {
                state.Add(rune);
            }

            var result = state.Complete();
            omittedFindingCount += result.OmittedFindingCount;
            hasHighConfidenceFinding |= result.HasHighConfidenceFinding;
            foreach (var finding in result.Findings)
            {
                if (!uniqueFindings.Add(finding))
                {
                    continue;
                }

                if (findings.Count < PackContentScanState.MaximumFindingCount)
                {
                    findings.Add(finding);
                    hasRetainedHighConfidenceFinding |=
                        finding.Severity == PackContentFindingSeverity.HighConfidence;
                }
                else
                {
                    if (
                        finding.Severity == PackContentFindingSeverity.HighConfidence
                        && !hasRetainedHighConfidenceFinding
                    )
                    {
                        findings[^1] = finding;
                        hasRetainedHighConfidenceFinding = true;
                    }

                    omittedFindingCount++;
                }
            }
        }

        return new PackContentScanResult(findings, omittedFindingCount, hasHighConfidenceFinding);
    }

    private static IEnumerable<IEnumerable<Rune>> DecodeCandidates(byte[] contents)
    {
        if (TryGetBomEncoding(contents, out var offset, out var unitBytes, out var littleEndian))
        {
            if (unitBytes == 4 && littleEndian && !IsValidUtf32(contents, offset, littleEndian))
            {
                yield return DecodeUtf16(contents, _utf16LittleEndianBom.Length, littleEndian);
            }

            yield return unitBytes == 2
                ? DecodeUtf16(contents, offset, littleEndian)
                : DecodeUtf32(contents, offset, littleEndian);
            yield break;
        }

        yield return DecodeUtf8(contents);
    }

    private static IEnumerable<Rune> DecodeUtf8(byte[] contents)
    {
        var utf8Offset = contents.AsSpan().StartsWith(_utf8Bom) ? _utf8Bom.Length : 0;
        while (utf8Offset < contents.Length)
        {
            var status = Rune.DecodeFromUtf8(
                contents.AsSpan(utf8Offset),
                out var rune,
                out var bytesConsumed
            );
            if (status == OperationStatus.Done)
            {
                yield return rune;
                utf8Offset += bytesConsumed;
                continue;
            }

            yield return new Rune(0);
            utf8Offset++;
        }
    }

    private static bool TryGetBomEncoding(
        byte[] contents,
        out int offset,
        out int unitBytes,
        out bool littleEndian
    )
    {
        var span = contents.AsSpan();
        if (span.StartsWith(_utf32LittleEndianBom))
        {
            offset = 4;
            unitBytes = 4;
            littleEndian = true;
            return true;
        }

        if (span.StartsWith(_utf32BigEndianBom))
        {
            offset = 4;
            unitBytes = 4;
            littleEndian = false;
            return true;
        }

        if (span.StartsWith(_utf16LittleEndianBom))
        {
            offset = 2;
            unitBytes = 2;
            littleEndian = true;
            return true;
        }

        if (span.StartsWith(_utf16BigEndianBom))
        {
            offset = 2;
            unitBytes = 2;
            littleEndian = false;
            return true;
        }

        offset = 0;
        unitBytes = 0;
        littleEndian = false;
        return false;
    }

    private static IEnumerable<Rune> DecodeUtf16(byte[] contents, int offset, bool littleEndian)
    {
        while (offset + 1 < contents.Length)
        {
            var first = ReadUInt16(contents, offset, littleEndian);
            offset += 2;
            if (char.IsHighSurrogate((char)first) && offset + 1 < contents.Length)
            {
                var second = ReadUInt16(contents, offset, littleEndian);
                if (char.IsLowSurrogate((char)second))
                {
                    yield return new Rune((char)first, (char)second);
                    offset += 2;
                    continue;
                }
            }

            if (char.IsSurrogate((char)first))
            {
                yield return new Rune(0);
                continue;
            }

            yield return new Rune(first);
        }

        if (offset < contents.Length)
        {
            yield return new Rune(0);
        }
    }

    private static IEnumerable<Rune> DecodeUtf32(byte[] contents, int offset, bool littleEndian)
    {
        while (offset + 3 < contents.Length)
        {
            var value = ReadUInt32(contents, offset, littleEndian);
            yield return Rune.TryCreate(unchecked((int)value), out var rune) ? rune : new Rune(0);
            offset += 4;
        }

        for (; offset < contents.Length; offset++)
        {
            yield return new Rune(0);
        }
    }

    private static bool IsValidUtf32(byte[] contents, int offset, bool littleEndian)
    {
        if ((contents.Length - offset) % 4 != 0)
        {
            return false;
        }

        while (offset < contents.Length)
        {
            var value = ReadUInt32(contents, offset, littleEndian);
            if (!Rune.TryCreate(unchecked((int)value), out _))
            {
                return false;
            }

            offset += 4;
        }

        return true;
    }

    private static ushort ReadUInt16(byte[] contents, int offset, bool littleEndian) =>
        littleEndian
            ? (ushort)(contents[offset] | contents[offset + 1] << 8)
            : (ushort)(contents[offset] << 8 | contents[offset + 1]);

    private static uint ReadUInt32(byte[] contents, int offset, bool littleEndian) =>
        littleEndian
            ? ReadUInt16(contents, offset, littleEndian)
                | (uint)ReadUInt16(contents, offset + 2, littleEndian) << 16
            : (uint)ReadUInt16(contents, offset, littleEndian) << 16
                | ReadUInt16(contents, offset + 2, littleEndian);
}
