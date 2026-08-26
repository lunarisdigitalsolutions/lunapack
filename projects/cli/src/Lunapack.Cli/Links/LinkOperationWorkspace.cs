using System.IO.Abstractions;

namespace Lunapack.Cli;

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

    public string Write(string sourcePath, byte[] contents)
    {
        var snapshotPath = _fileSystem.Path.Combine(
            Directory,
            ProjectPath.Normalize(sourcePath).Replace('/', _fileSystem.Path.DirectorySeparatorChar)
        );
        var snapshotDirectory = _fileSystem.Path.GetDirectoryName(snapshotPath);
        if (!string.IsNullOrEmpty(snapshotDirectory))
        {
            _fileSystem.Directory.CreateDirectory(snapshotDirectory);
        }

        _fileSystem.File.WriteAllBytes(snapshotPath, contents);
        return snapshotPath;
    }

    public byte[] Read(string snapshotPath) => _fileSystem.File.ReadAllBytes(snapshotPath);

    public void Dispose() => GitTemporaryWorkspace.Delete(_fileSystem, Directory);
}
