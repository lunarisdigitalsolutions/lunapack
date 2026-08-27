namespace Lunapack.Cli;

internal sealed record ExternalSourceAliasMapping(
    string PackId,
    string PackVersion,
    string Alias,
    string WorkspaceSourceName,
    SourceFingerprint Fingerprint
);
