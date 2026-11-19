namespace Lunapack.Cli.Packs.ContentScreening;

internal sealed record PackContentScanResult(
    IReadOnlyList<PackContentFinding> Findings,
    long OmittedFindingCount,
    bool HasHighConfidenceFinding
);
