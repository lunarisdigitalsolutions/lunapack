using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Links;

internal sealed class LinkOperationWorkspace : IDisposable
{
    private readonly IFileSystem _fileSystem;

    private LinkOperationWorkspace(IFileSystem fileSystem, string directory)
    {
        _fileSystem = fileSystem;
        Directory = directory;
    }

    public string Directory { get; }

    public static LinkOperationWorkspace Create(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var directory = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            "lunapack",
            "links",
            Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)
        );
        fileSystem.Directory.CreateDirectory(directory);
        return new LinkOperationWorkspace(fileSystem, directory);
    }

    public async Task<ManifestOperationResult<string>> WriteAsync(
        string sourcePath,
        Stream source,
        long expectedLength,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);
        var snapshotPath = _fileSystem.Path.Combine(
            Directory,
            ProjectPath.Normalize(sourcePath).Replace('/', _fileSystem.Path.DirectorySeparatorChar)
        );
        var snapshotDirectory = _fileSystem.Path.GetDirectoryName(snapshotPath);
        if (!string.IsNullOrEmpty(snapshotDirectory))
        {
            _fileSystem.Directory.CreateDirectory(snapshotDirectory);
        }

        var completed = false;
        try
        {
            await using var destination = _fileSystem.File.Open(
                snapshotPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );
            var buffer = new byte[81920];
            long bytesWritten = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                bytesWritten = checked(bytesWritten + read);
                if (bytesWritten > expectedLength)
                {
                    return ManifestOperationResult<string>.Failure(
                        $"Link source file '{sourcePath}' changed while its snapshot was being created."
                    );
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (bytesWritten != expectedLength)
            {
                return ManifestOperationResult<string>.Failure(
                    $"Link source file '{sourcePath}' changed while its snapshot was being created."
                );
            }

            completed = true;
            return ManifestOperationResult<string>.Success(snapshotPath);
        }
        finally
        {
            if (!completed && _fileSystem.File.Exists(snapshotPath))
            {
                _fileSystem.File.Delete(snapshotPath);
            }
        }
    }

    public byte[] Read(string snapshotPath) => _fileSystem.File.ReadAllBytes(snapshotPath);

    public void Dispose() => GitTemporaryWorkspace.Delete(_fileSystem, Directory);
}
