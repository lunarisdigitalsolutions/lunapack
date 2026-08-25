using System.IO.Abstractions;

namespace Lunapack.Cli;

internal interface IOperationSnapshotSecurity
{
    void ApplyDirectory(string path);

    void ApplyFile(string path);

    void MakeReadOnly(IFileSystem fileSystem, string root);

    void PrepareForDelete(IFileSystem fileSystem, string root);
}
