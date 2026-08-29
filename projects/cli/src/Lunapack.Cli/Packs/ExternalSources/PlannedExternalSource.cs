namespace Lunapack.Cli.Packs.ExternalSources;

internal sealed record PlannedExternalSource(
    string Alias,
    string SourceName,
    string Fingerprint,
    string SourcePath,
    string Ref,
    string ResolvedCommit
);
