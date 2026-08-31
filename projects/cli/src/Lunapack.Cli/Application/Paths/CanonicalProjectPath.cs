using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Application.Paths;

internal static class CanonicalProjectPath
{
    public static ManifestOperationResult<string> Resolve(
        IFileSystem fileSystem,
        string projectDirectory
    )
    {
        try
        {
            var fullPath = fileSystem.Path.GetFullPath(projectDirectory);
            if (!fileSystem.Directory.Exists(fullPath))
            {
                return ManifestOperationResult<string>.Failure(
                    $"Project directory '{projectDirectory}' does not exist."
                );
            }

            var physicalPath = ResolveLinks(fileSystem, fullPath);
            return ManifestOperationResult<string>.Success(
                ProjectPath.Normalize(fileSystem.Path.TrimEndingDirectorySeparator(physicalPath))
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return ManifestOperationResult<string>.Failure(
                $"Unable to resolve project directory '{projectDirectory}': {exception.Message}"
            );
        }
    }

    private static string ResolveLinks(IFileSystem fileSystem, string fullPath)
    {
        var root =
            fileSystem.Path.GetPathRoot(fullPath)
            ?? throw new IOException("Project directory has no filesystem root.");
        var currentPath = root;
        var relativePath = fileSystem.Path.GetRelativePath(root, fullPath);
        var segments = relativePath.Split(
            [fileSystem.Path.DirectorySeparatorChar, fileSystem.Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries
        );
        foreach (var segment in segments)
        {
            currentPath = fileSystem.Path.Combine(currentPath, segment);
            if (!IsReparsePoint(fileSystem, currentPath))
            {
                continue;
            }

            var target =
                Directory.ResolveLinkTarget(currentPath, returnFinalTarget: true)
                ?? throw new IOException($"Unable to resolve directory link '{currentPath}'.");
            currentPath = target.FullName;
        }

        return fileSystem.Path.GetFullPath(currentPath);
    }

    private static bool IsReparsePoint(IFileSystem fileSystem, string path) =>
        fileSystem.File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
