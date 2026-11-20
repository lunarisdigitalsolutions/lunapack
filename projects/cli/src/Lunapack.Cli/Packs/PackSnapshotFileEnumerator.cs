using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;

namespace Lunapack.Cli.Packs;

internal static class PackSnapshotFileEnumerator
{
    public static ManifestOperationResult<List<string>> Enumerate(
        IFileSystem fileSystem,
        string packDirectory,
        string packId,
        ResourceLimits resourceLimits
    )
    {
        var budget = new PackSnapshotBudget(packId, resourceLimits);
        var files = new List<string>();
        try
        {
            foreach (
                var path in fileSystem.Directory.EnumerateFileSystemEntries(
                    packDirectory,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                var attributes = fileSystem.File.GetAttributes(path);
                ManifestOperationResult<bool> observed;
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    observed = budget.AddDirectory();
                }
                else
                {
                    observed = budget.AddFile(fileSystem.FileInfo.New(path).Length);
                }

                if (!observed.IsSuccess)
                {
                    return ManifestOperationResult<List<string>>.Failure(
                        observed.Error
                            ?? $"Pack '{packId}' snapshot exceeds its configured limits.",
                        observed.ErrorKind
                    );
                }

                if (!attributes.HasFlag(FileAttributes.Directory))
                {
                    files.Add(
                        ProjectPath.Normalize(fileSystem.Path.GetRelativePath(packDirectory, path))
                    );
                }
            }

            return ManifestOperationResult<List<string>>.Success(files);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return ManifestOperationResult<List<string>>.Failure(
                $"Unable to enumerate pack '{packId}' snapshot: {exception.Message}"
            );
        }
    }
}
