using System.ComponentModel;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Application.Paths;

internal static class CanonicalProjectPath
{
    private const uint CreateFileOpenExisting = 3;
    private const uint CreateFileFlagBackupSemantics = 0x02000000;

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

            var linkedPath = ResolveLinks(fileSystem, fullPath);
            var physicalPath =
                OperatingSystem.IsWindows() && fileSystem is FileSystem
                    ? ResolveWindowsFinalPath(linkedPath)
                    : linkedPath;
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
                        or Win32Exception
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

    private static string ResolveWindowsFinalPath(string path)
    {
        using var handle = CanonicalProjectPathNativeMethods.CreateFile(
            path,
            desiredAccess: 0,
            shareMode: FileShare.ReadWrite | FileShare.Delete,
            securityAttributes: nint.Zero,
            creationDisposition: CreateFileOpenExisting,
            flagsAndAttributes: CreateFileFlagBackupSemantics,
            templateFile: nint.Zero
        );
        var capacity = 512;
        while (true)
        {
            var buffer = new char[capacity];
            var length = CanonicalProjectPathNativeMethods.GetFinalPathNameByHandle(
                handle,
                buffer,
                (uint)buffer.Length,
                0
            );
            if (length == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Unable to resolve the final path for '{path}'."
                );
            }

            if (length < buffer.Length)
            {
                return StripWindowsNamespacePrefix(new string(buffer, 0, (int)length));
            }

            capacity = checked((int)length + 1);
        }
    }

    private static string StripWindowsNamespacePrefix(string path)
    {
        const string ExtendedPathPrefix = @"\\?\";
        const string ExtendedUncPrefix = @"\\?\UNC\";
        if (path.StartsWith(ExtendedUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[ExtendedUncPrefix.Length..];
        }

        return path.StartsWith(ExtendedPathPrefix, StringComparison.OrdinalIgnoreCase)
            ? path[ExtendedPathPrefix.Length..]
            : path;
    }
}
