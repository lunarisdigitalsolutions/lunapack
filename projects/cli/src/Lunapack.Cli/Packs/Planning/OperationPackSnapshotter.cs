using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class OperationPackSnapshotter(
    IFileSystem fileSystem,
    IOperationSnapshotSecurity snapshotSecurity,
    CliConsole console
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

            RejectPackDirectoryLink(sourcePath, sourcePackDirectory);

            var packPath = fileSystem.Path.GetRelativePath(sourcePath, sourcePackDirectory);
            var snapshotPackDirectory = CreateSnapshotPackDirectory(snapshotRoot, packPath);
            CopyEntries(sourcePackDirectory, sourcePackDirectory, snapshotPackDirectory);
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

    private void CopyEntries(
        string sourcePackDirectory,
        string sourceDirectory,
        string snapshotDirectory
    )
    {
        var sourceEntries = fileSystem
            .Directory.EnumerateFileSystemEntries(
                sourceDirectory,
                "*",
                SearchOption.TopDirectoryOnly
            )
            .OrderBy(path => path, StringComparer.Ordinal);
        foreach (var sourceEntry in sourceEntries)
        {
            var attributes = fileSystem.File.GetAttributes(sourceEntry);
            if (
                attributes.HasFlag(FileAttributes.ReparsePoint)
                || attributes.HasFlag(FileAttributes.Device)
            )
            {
                console.Warning(
                    $"Skipping unsupported pack snapshot entry '{GetRelativePath(sourcePackDirectory, sourceEntry)}'; only regular files and directories are copied."
                );
                continue;
            }

            var destination = fileSystem.Path.Combine(
                snapshotDirectory,
                fileSystem.Path.GetFileName(sourceEntry)
            );
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                fileSystem.Directory.CreateDirectory(destination);
                snapshotSecurity.ApplyDirectory(destination);
                CopyEntries(sourcePackDirectory, sourceEntry, destination);
                continue;
            }

            fileSystem.File.Copy(sourceEntry, destination);
            snapshotSecurity.ApplyFile(destination);
        }
    }

    private void RejectPackDirectoryLink(string sourcePath, string sourcePackDirectory)
    {
        if (fileSystem.File.GetAttributes(sourcePackDirectory).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException(
                $"Pack snapshot root '{GetRelativePath(sourcePath, sourcePackDirectory)}' cannot be a link or reparse point."
            );
        }
    }

    private string GetRelativePath(string root, string path) =>
        ProjectPath.Normalize(fileSystem.Path.GetRelativePath(root, path));
}
