namespace Lunapack.Cli;

internal sealed record ResolvedLinkFile(
    string SourcePath,
    string DeclaredTargetPath,
    string TargetPath,
    string Sha256,
    string SnapshotPath
);
