namespace Lunapack.Cli;

internal sealed class LinkResolution(
    LinkOperationWorkspace workspace,
    ResolvedLinkSnapshot snapshot
) : IDisposable
{
    public ResolvedLinkSnapshot Snapshot { get; } = snapshot;

    public byte[] ReadContents(ResolvedLinkFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return workspace.Read(file.SnapshotPath);
    }

    public void Dispose() => workspace.Dispose();
}
