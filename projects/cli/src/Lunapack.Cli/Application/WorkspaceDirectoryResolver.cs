using System.IO.Abstractions;

namespace Lunapack.Cli.Application;

internal sealed class WorkspaceDirectoryResolver(IFileSystem fileSystem)
{
    public string Resolve(string projectDirectory, string? workspace) =>
        string.IsNullOrWhiteSpace(workspace)
            ? projectDirectory
            : fileSystem.Path.GetFullPath(workspace, projectDirectory);
}
