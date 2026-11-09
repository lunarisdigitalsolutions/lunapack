namespace Lunapack.Cli.Packs.Publishing;

internal sealed record PackPublicationResult(
    string PackId,
    string Version,
    string SourceName,
    string Reference,
    string ManifestDigest
);
