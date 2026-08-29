namespace Lunapack.Cli.Links;

internal sealed record LinkSummary(
    string Name,
    string Source,
    string Target,
    bool IsInstalled,
    int SelectedFileCount,
    int ModifiedFileCount
)
{
    public const string WorkspaceRootTarget = "<workspace root>";

    public string Status =>
        !IsInstalled ? "not installed"
        : ModifiedFileCount > 0 ? "modified"
        : "installed";
}
