using System.IO.Abstractions;

namespace Lunapack.Cli.Application.Paths;

internal static class ProjectMutationPathSecurity
{
    public static void EnsureNoAliases(
        IFileSystem fileSystem,
        string projectDirectory,
        string targetPath
    ) => EnsureNoAliases(fileSystem, projectDirectory, targetPath, includeTarget: true);

    public static void ReplaceFile(
        IFileSystem fileSystem,
        string projectDirectory,
        string targetPath,
        ReadOnlySpan<byte> contents
    )
    {
        EnsureNoAliases(fileSystem, projectDirectory, targetPath, includeTarget: false);
        var temporaryPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (
                var stream = fileSystem.File.Open(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                stream.Write(contents);
            }

            fileSystem.File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (fileSystem.File.Exists(temporaryPath))
            {
                fileSystem.File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureNoAliases(
        IFileSystem fileSystem,
        string projectDirectory,
        string targetPath,
        bool includeTarget
    )
    {
        var projectPath = fileSystem.Path.GetFullPath(projectDirectory);
        var resolvedTargetPath = fileSystem.Path.GetFullPath(targetPath);
        var relativeTargetPath = fileSystem.Path.GetRelativePath(projectPath, resolvedTargetPath);
        var normalized = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectPath,
            relativeTargetPath
        );
        if (normalized.Value is not { } projectRelativePath)
        {
            throw new IOException(
                $"Managed path '{ProjectPath.Normalize(relativeTargetPath)}' must remain inside the project directory."
            );
        }

        var currentPath = projectPath;
        var segments = projectRelativePath.Split('/');
        var segmentCount = includeTarget ? segments.Length : segments.Length - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            currentPath = fileSystem.Path.Combine(currentPath, segments[index]);
            if (!TryGetAttributes(fileSystem, currentPath, out var attributes))
            {
                break;
            }

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var aliasPath = ProjectPath.Normalize(
                    fileSystem.Path.GetRelativePath(projectPath, currentPath)
                );
                throw new IOException(
                    $"Managed path '{projectRelativePath}' crosses link or reparse point '{aliasPath}'."
                );
            }
        }
    }

    private static bool TryGetAttributes(
        IFileSystem fileSystem,
        string path,
        out FileAttributes attributes
    )
    {
        try
        {
            attributes = fileSystem.File.GetAttributes(path);
            return true;
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }
}
