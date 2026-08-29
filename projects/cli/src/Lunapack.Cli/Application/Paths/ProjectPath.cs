using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Application.Paths;

internal static class ProjectPath
{
    public static string Normalize(string path) => path.Replace('\\', '/');

    public static string? NormalizeOptional(string? path) => path?.Replace('\\', '/');

    public static ManifestOperationResult<string> NormalizeProjectRelativePath(
        IFileSystem fileSystem,
        string projectDirectory,
        string path
    )
    {
        var normalizedPath = Normalize(path);
        if (normalizedPath.Length == 0 || IsRooted(fileSystem, normalizedPath))
        {
            return ManifestOperationResult<string>.Failure(
                "Path must be a non-empty path relative to the project directory."
            );
        }

        var projectPath = fileSystem.Path.GetFullPath(projectDirectory);
        var resolvedPath = fileSystem.Path.GetFullPath(normalizedPath, projectPath);
        var comparison =
            fileSystem.Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        var projectPathWithSeparator = projectPath.EndsWith(
            fileSystem.Path.DirectorySeparatorChar.ToString(),
            comparison
        )
            ? projectPath
            : $"{projectPath}{fileSystem.Path.DirectorySeparatorChar}";
        if (
            !string.Equals(resolvedPath, projectPath, comparison)
            && !resolvedPath.StartsWith(projectPathWithSeparator, comparison)
        )
        {
            return ManifestOperationResult<string>.Failure(
                "Path must resolve within the project directory."
            );
        }

        return ManifestOperationResult<string>.Success(
            Normalize(fileSystem.Path.GetRelativePath(projectPath, resolvedPath)).TrimEnd('/')
        );
    }

    private static bool IsRooted(IFileSystem fileSystem, string path) =>
        fileSystem.Path.IsPathRooted(path)
        || path.StartsWith("//", StringComparison.Ordinal)
        || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');
}
