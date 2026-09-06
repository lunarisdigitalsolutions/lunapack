namespace Lunapack.Cli.Packs;

internal sealed record PackUpdateOptions
{
    public IReadOnlyList<string> Parameters { get; init; } = [];

    public bool NoVariables { get; init; }

    public IReadOnlyList<string> SkippedVariables { get; init; } = [];

    public IReadOnlyList<string> DirectoryRemappings { get; init; } = [];

    public IReadOnlyList<string> FileRemappings { get; init; } = [];

    public bool SaveRemapping { get; init; }

    public bool HasRemappings => DirectoryRemappings.Count > 0 || FileRemappings.Count > 0;
}
