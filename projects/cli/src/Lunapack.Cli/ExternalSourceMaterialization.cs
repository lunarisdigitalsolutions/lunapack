using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed class ExternalSourceMaterialization(
    IFileSystem fileSystem,
    ExternalContentRoots roots,
    string? workspace,
    IOperationSnapshotSecurity snapshotSecurity
) : IAsyncDisposable
{
    public ExternalContentRoots Roots { get; } = roots;

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
