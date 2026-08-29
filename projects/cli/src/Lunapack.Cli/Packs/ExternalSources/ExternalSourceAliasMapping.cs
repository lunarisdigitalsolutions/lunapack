using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Packs.ExternalSources;

internal sealed record ExternalSourceAliasMapping(
    string PackId,
    string PackVersion,
    string Alias,
    string WorkspaceSourceName,
    SourceFingerprint Fingerprint
);
