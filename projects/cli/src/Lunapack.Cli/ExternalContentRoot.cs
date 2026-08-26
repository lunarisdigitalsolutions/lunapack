namespace Lunapack.Cli;

internal sealed record ExternalContentRoot(
    string Alias,
    string Directory,
    string SourceName,
    string Fingerprint,
    string Ref,
    string ResolvedCommit
);
