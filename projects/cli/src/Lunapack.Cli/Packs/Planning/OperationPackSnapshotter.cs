using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;
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
        string snapshotRoot,
        ResourceLimits? resourceLimits = null
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
            var copied = CopyEntries(
                sourcePackDirectory,
                sourcePackDirectory,
                snapshotPackDirectory,
                new PackSnapshotBudget(pack.Manifest.Id, resourceLimits ?? ResourceLimits.Default)
            );
            if (!copied.IsSuccess)
            {
                snapshotSecurity.PrepareForDelete(fileSystem, snapshotRoot);
                GitTemporaryWorkspace.Delete(fileSystem, snapshotRoot);
                return ManifestOperationResult<DiscoveredPack>.Failure(
                    copied.Error ?? "Unable to snapshot resolved pack.",
                    copied.ErrorKind
                );
            }

            snapshotSecurity.MakeReadOnly(fileSystem, snapshotRoot);

            return ManifestOperationResult<DiscoveredPack>.Success(
                pack with
                {
                    SourcePath = snapshotRoot,
                    PackDirectory = snapshotPackDirectory,
                }
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or OverflowException)
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

    private ManifestOperationResult<bool> CopyEntries(
        string sourcePackDirectory,
        string sourceDirectory,
        string snapshotDirectory,
        PackSnapshotBudget budget
    )
    {
        var directories = new Stack<(string Source, string Target)>();
        directories.Push((sourceDirectory, snapshotDirectory));
        while (directories.TryPop(out var current))
        {
            foreach (
                var sourceEntry in fileSystem.Directory.EnumerateFileSystemEntries(
                    current.Source,
                    "*",
                    SearchOption.TopDirectoryOnly
                )
            )
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
                    current.Target,
                    fileSystem.Path.GetFileName(sourceEntry)
                );
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    var addedDirectory = budget.AddDirectory();
                    if (!addedDirectory.IsSuccess)
                    {
                        return addedDirectory;
                    }

                    fileSystem.Directory.CreateDirectory(destination);
                    snapshotSecurity.ApplyDirectory(destination);
                    directories.Push((sourceEntry, destination));
                    continue;
                }

                var copied = CopyFile(sourceEntry, destination, budget);
                if (!copied.IsSuccess)
                {
                    return copied;
                }
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private ManifestOperationResult<bool> CopyFile(
        string sourcePath,
        string destinationPath,
        PackSnapshotBudget budget
    )
    {
        var registered = budget.AddFile();
        if (!registered.IsSuccess)
        {
            return registered;
        }

        using (var source = fileSystem.File.OpenRead(sourcePath))
        using (
            var destination = fileSystem.File.Open(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            )
        )
        {
            var buffer = new byte[81_920];
            while (true)
            {
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                var added = budget.AddBytes(read);
                if (!added.IsSuccess)
                {
                    return added;
                }

                destination.Write(buffer, 0, read);
            }
        }

        snapshotSecurity.ApplyFile(destinationPath);
        return ManifestOperationResult<bool>.Success(true);
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
