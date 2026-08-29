namespace Lunapack.Cli.Links;

internal sealed record LinkFileMapping(
    string SourcePath,
    string DeclaredTargetPath,
    string TargetPath
);
