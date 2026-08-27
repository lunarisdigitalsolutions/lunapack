namespace Lunapack.Cli;

internal sealed record ManagedRootFile(
    string SourcePath,
    string DeclaredTargetPath,
    string TargetPath,
    string Sha256
);
