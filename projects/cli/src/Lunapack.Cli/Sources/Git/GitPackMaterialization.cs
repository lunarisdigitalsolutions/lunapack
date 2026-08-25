using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed class GitPackMaterialization(
    IFileSystem fileSystem,
    ResolvedPackGraph graph,
    string? workspace,
    IOperationSnapshotSecurity snapshotSecurity
) : IAsyncDisposable
{
    public ResolvedPackGraph Graph { get; } = graph;

    public ValueTask DisposeAsync()
    {
        if (workspace is not null)
        {
            snapshotSecurity.PrepareForDelete(fileSystem, workspace);
        }
        GitTemporaryWorkspace.Delete(fileSystem, workspace);

        return ValueTask.CompletedTask;
    }
}
