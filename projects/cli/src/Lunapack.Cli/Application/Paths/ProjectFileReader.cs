using System.IO.Abstractions;

namespace Lunapack.Cli.Application.Paths;

internal static class ProjectFileReader
{
    public static Stream OpenRead(
        IFileSystem fileSystem,
        string projectDirectory,
        string projectRelativePath
    )
    {
        var normalized = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            projectRelativePath
        );
        if (normalized.Value is not { } targetPath)
        {
            throw new IOException(
                $"Managed target '{projectRelativePath}' is not a safe project-relative path: {normalized.Error}"
            );
        }

        var projectPath = fileSystem.Path.GetFullPath(projectDirectory);
        var fullTargetPath = fileSystem.Path.GetFullPath(targetPath, projectPath);
        ProjectMutationPathSecurity.EnsureNoAliases(fileSystem, projectPath, fullTargetPath);
        var stream = fileSystem.File.Open(
            fullTargetPath,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.Read,
            }
        );
        var validated = false;
        try
        {
            ProjectMutationPathSecurity.EnsureNoAliases(fileSystem, projectPath, fullTargetPath);
            validated = true;
            return stream;
        }
        finally
        {
            if (!validated)
            {
                stream.Dispose();
            }
        }
    }
}
