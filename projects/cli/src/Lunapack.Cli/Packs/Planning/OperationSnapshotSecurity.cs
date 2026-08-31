using System.IO.Abstractions;
using System.Runtime.Versioning;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class OperationSnapshotSecurity : IOperationSnapshotSecurity
{
    public void ApplyDirectory(string path) =>
        UserSettingsPathSecurity.Apply(path, directory: true);

    public void ApplyFile(string path) => UserSettingsPathSecurity.Apply(path, directory: false);

    public void MakeReadOnly(IFileSystem fileSystem, string root)
    {
        var files = fileSystem.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            if (OperatingSystem.IsWindows())
            {
                fileSystem.File.SetAttributes(file, FileAttributes.ReadOnly);
            }
            else
            {
                SetUnixFileReadOnly(file);
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (var directory in EnumerateDirectories(fileSystem, root))
            {
                SetUnixDirectoryReadOnly(directory);
            }
        }
    }

    public void PrepareForDelete(IFileSystem fileSystem, string root)
    {
        if (OperatingSystem.IsWindows() || !fileSystem.Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in EnumerateDirectories(fileSystem, root))
        {
            SetUnixDirectoryWritable(directory);
        }
    }

    private static IEnumerable<string> EnumerateDirectories(IFileSystem fileSystem, string root) =>
        fileSystem
            .Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Prepend(root);

    [UnsupportedOSPlatform("windows")]
    private static void SetUnixFileReadOnly(string path) =>
        File.SetUnixFileMode(path, UnixFileMode.UserRead);

    [UnsupportedOSPlatform("windows")]
    private static void SetUnixDirectoryReadOnly(string path) =>
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);

    [UnsupportedOSPlatform("windows")]
    private static void SetUnixDirectoryWritable(string path) =>
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
}
