using System.Globalization;
using System.Text;

namespace Lunapack.Cli.Packs.ContentScreening;

internal sealed class PackContentScanState(string path)
{
    public const int MaximumFindingCount = 1000;

    private readonly List<PackContentFinding> _findings = [];
    private long _omittedFindingCount;
    private bool _hasHighConfidenceFinding;
    private bool _hasRetainedHighConfidenceFinding;
    private int _line = 1;
    private int _column = 1;
    private bool _previousWasCarriageReturn;
    private bool _previousWasEmojiElement;
    private bool _previousCanTakeVariationSelector;
    private PackContentFinding? _pendingEmojiJoiner;

    public void Add(Rune rune)
    {
        ResolvePendingEmojiJoiner(rune);
        if (HandleLineBreak(rune))
        {
            return;
        }

        _previousWasCarriageReturn = false;
        AddFinding(rune);
        UpdateEmojiState(rune);
        _column++;
    }

    public PackContentScanResult Complete()
    {
        if (_pendingEmojiJoiner is { } pending)
        {
            AddFinding(pending);
            _pendingEmojiJoiner = null;
        }

        return new PackContentScanResult(
            _findings,
            _omittedFindingCount,
            _hasHighConfidenceFinding
        );
    }

    private void ResolvePendingEmojiJoiner(Rune rune)
    {
        if (_pendingEmojiJoiner is null)
        {
            return;
        }

        if (!EmojiUnicodeData.IsExtendedPictographic(rune))
        {
            AddFinding(_pendingEmojiJoiner);
        }

        _pendingEmojiJoiner = null;
    }

    private bool HandleLineBreak(Rune rune)
    {
        if (rune.Value == '\r')
        {
            _line++;
            _column = 1;
            _previousWasCarriageReturn = true;
            ResetEmojiState();
            return true;
        }

        if (rune.Value != '\n')
        {
            return false;
        }

        if (!_previousWasCarriageReturn)
        {
            _line++;
            _column = 1;
        }

        _previousWasCarriageReturn = false;
        ResetEmojiState();
        return true;
    }

    private void AddFinding(Rune rune)
    {
        if (
            rune.Value == 0x200D
            && Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format
            && _previousWasEmojiElement
        )
        {
            _pendingEmojiJoiner = CreateFinding(rune, PackContentFindingSeverity.Warning);
            return;
        }

        if (GetSeverity(rune) is { } severity)
        {
            AddFinding(CreateFinding(rune, severity));
            return;
        }

        if (EmojiUnicodeData.IsVariationSelector(rune) && !_previousCanTakeVariationSelector)
        {
            AddFinding(CreateFinding(rune, PackContentFindingSeverity.Warning));
        }
    }

    private void AddFinding(PackContentFinding finding)
    {
        var isHighConfidence = finding.Severity == PackContentFindingSeverity.HighConfidence;
        _hasHighConfidenceFinding |= isHighConfidence;
        if (_findings.Count < MaximumFindingCount)
        {
            _findings.Add(finding);
            _hasRetainedHighConfidenceFinding |= isHighConfidence;
            return;
        }

        if (isHighConfidence && !_hasRetainedHighConfidenceFinding)
        {
            _findings[^1] = finding;
            _hasRetainedHighConfidenceFinding = true;
        }

        _omittedFindingCount++;
    }

    private PackContentFinding CreateFinding(Rune rune, PackContentFindingSeverity severity) =>
        new(path, _line, _column, rune.Value, severity);

    private void UpdateEmojiState(Rune rune)
    {
        _previousWasEmojiElement =
            EmojiUnicodeData.IsExtendedPictographic(rune)
            || EmojiUnicodeData.IsExtend(rune) && _previousWasEmojiElement;
        _previousCanTakeVariationSelector =
            _previousWasEmojiElement
            || EmojiUnicodeData.IsKeycapBase(rune)
            || EmojiUnicodeData.IsVariationSelector(rune) && _previousCanTakeVariationSelector;
    }

    private void ResetEmojiState()
    {
        _previousWasEmojiElement = false;
        _previousCanTakeVariationSelector = false;
    }

    private static PackContentFindingSeverity? GetSeverity(Rune rune)
    {
        if (IsBidiControl(rune.Value) || rune.Value == 0x200B)
        {
            return PackContentFindingSeverity.HighConfidence;
        }

        return Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format
            ? PackContentFindingSeverity.Warning
            : null;
    }

    private static bool IsBidiControl(int value) =>
        value is 0x061C or 0x200E or 0x200F or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069;
}
