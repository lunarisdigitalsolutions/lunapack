using System.IO.Abstractions;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Packs.ExternalSources;

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
