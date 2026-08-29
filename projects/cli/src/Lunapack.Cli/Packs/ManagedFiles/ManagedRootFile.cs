namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record ManagedRootFile(
    string SourcePath,
    string DeclaredTargetPath,
    string TargetPath,
    string Sha256
);
