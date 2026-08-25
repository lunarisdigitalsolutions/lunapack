using System.IO.Abstractions;

namespace Lunapack.Cli;

internal static class GitTemporaryWorkspace
{
    public static void Delete(IFileSystem fileSystem, string? workspace)
    {
        if (workspace is null || !fileSystem.Directory.Exists(workspace))
        {
            return;
        }

        try
        {
            foreach (
                var filePath in fileSystem.Directory.EnumerateFiles(
                    workspace,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                fileSystem.File.SetAttributes(filePath, FileAttributes.Normal);
            }

            fileSystem.Directory.Delete(workspace, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { }
    }
}
