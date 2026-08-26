namespace Lunapack.Cli;

internal sealed record LinkFileMapping(
    string SourcePath,
    string DeclaredTargetPath,
    string TargetPath
);
