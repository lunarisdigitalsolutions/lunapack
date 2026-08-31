using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class OperationPackSnapshotter(
    IFileSystem fileSystem,
    IOperationSnapshotSecurity snapshotSecurity
)
{
    public ManifestOperationResult<DiscoveredPack> Snapshot(
        DiscoveredPack pack,
        string snapshotRoot
    )
    {
        try
        {
            var sourcePath = fileSystem.Path.GetFullPath(pack.SourcePath);
            var sourcePackDirectory = fileSystem.Path.GetFullPath(pack.PackDirectory);
            if (!fileSystem.Directory.Exists(sourcePackDirectory))
            {
                return ManifestOperationResult<DiscoveredPack>.Failure(
                    $"Pack directory '{pack.PackDirectory}' is unavailable for snapshotting."
                );
            }

            var packPath = fileSystem.Path.GetRelativePath(sourcePath, sourcePackDirectory);
            var snapshotPackDirectory = CreateSnapshotPackDirectory(snapshotRoot, packPath);
            CopyDirectories(sourcePackDirectory, snapshotPackDirectory);
            CopyFiles(sourcePackDirectory, snapshotPackDirectory);
            snapshotSecurity.MakeReadOnly(fileSystem, snapshotRoot);

            return ManifestOperationResult<DiscoveredPack>.Success(
                pack with
                {
                    SourcePath = snapshotRoot,
                    PackDirectory = snapshotPackDirectory,
                }
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            snapshotSecurity.PrepareForDelete(fileSystem, snapshotRoot);
            GitTemporaryWorkspace.Delete(fileSystem, snapshotRoot);
            return ManifestOperationResult<DiscoveredPack>.Failure(
                $"Unable to snapshot pack '{pack.Manifest.Id}': {exception.Message}"
            );
        }
    }

    private string CreateSnapshotPackDirectory(string snapshotRoot, string packPath)
    {
        var snapshotPackDirectory =
            string.IsNullOrEmpty(packPath) || string.Equals(packPath, ".", StringComparison.Ordinal)
                ? snapshotRoot
                : fileSystem.Path.GetFullPath(packPath, snapshotRoot);
        fileSystem.Directory.CreateDirectory(snapshotPackDirectory);
        snapshotSecurity.ApplyDirectory(snapshotPackDirectory);
        return snapshotPackDirectory;
    }

    private void CopyDirectories(string sourcePackDirectory, string snapshotPackDirectory)
    {
        var sourceDirectories = fileSystem
            .Directory.EnumerateDirectories(sourcePackDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);
        foreach (var sourceDirectory in sourceDirectories)
        {
            var directoryPath = fileSystem.Path.GetRelativePath(
                sourcePackDirectory,
                sourceDirectory
            );
            var snapshotDirectory = fileSystem.Path.GetFullPath(
                directoryPath,
                snapshotPackDirectory
            );
            fileSystem.Directory.CreateDirectory(snapshotDirectory);
            snapshotSecurity.ApplyDirectory(snapshotDirectory);
        }
    }

    private void CopyFiles(string sourcePackDirectory, string snapshotPackDirectory)
    {
        var sourceFiles = fileSystem
            .Directory.EnumerateFiles(sourcePackDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);
        foreach (var sourceFile in sourceFiles)
        {
            var filePath = fileSystem.Path.GetRelativePath(sourcePackDirectory, sourceFile);
            var snapshotFile = fileSystem.Path.GetFullPath(filePath, snapshotPackDirectory);
            fileSystem.File.Copy(sourceFile, snapshotFile);
            snapshotSecurity.ApplyFile(snapshotFile);
        }
    }
}
