using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.Planning;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class OciTemporaryWorkspace : IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly IOperationSnapshotSecurity _security;

    private OciTemporaryWorkspace(
        IFileSystem fileSystem,
        IOperationSnapshotSecurity security,
        string path
    )
    {
        _fileSystem = fileSystem;
        _security = security;
        Path = path;
    }

    public string Path { get; }

    public static ManifestOperationResult<OciTemporaryWorkspace> Create(
        IFileSystem fileSystem,
        IOperationSnapshotSecurity? security = null
    )
    {
        var workspace = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            "lunapack",
            "oci",
            Guid.NewGuid().ToString("N")
        );
        var snapshotSecurity = security ?? new OperationSnapshotSecurity();
        try
        {
            fileSystem.Directory.CreateDirectory(workspace);
            snapshotSecurity.ApplyDirectory(workspace);
            return ManifestOperationResult<OciTemporaryWorkspace>.Success(
                new OciTemporaryWorkspace(fileSystem, snapshotSecurity, workspace)
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<OciTemporaryWorkspace>.Failure(
                $"Unable to create a private OCI temporary workspace: {exception.Message}"
            );
        }
    }

    public void Dispose()
    {
        if (!_fileSystem.Directory.Exists(Path))
        {
            return;
        }

        _security.PrepareForDelete(_fileSystem, Path);
        foreach (
            var file in _fileSystem.Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
        )
        {
            _fileSystem.File.SetAttributes(file, FileAttributes.Normal);
        }

        try
        {
            _fileSystem.Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Unable to clean up the OCI temporary workspace.", exception);
        }
    }
}
