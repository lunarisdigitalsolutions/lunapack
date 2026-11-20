using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Lunapack.Cli.Doctor;

internal sealed class DoctorTemporaryFileProbe(IFileSystem fileSystem) : IDoctorTemporaryFileProbe
{
    public DoctorTemporaryFileProbeResult Probe(string directory)
    {
        var normalizedDirectory = fileSystem.Path.GetFullPath(directory);
        var boundaryError = ValidateDirectoryBoundary(normalizedDirectory);
        if (boundaryError is not null)
        {
            return new DoctorTemporaryFileProbeResult(false, boundaryError);
        }

        var probePath = fileSystem.Path.Combine(
            normalizedDirectory,
            $".lunapack-doctor-{RandomNumberGenerator.GetHexString(32, lowercase: true)}.tmp"
        );
        FileStream? probe = null;
        try
        {
            probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose
            );
            probe.WriteByte(0);
            probe.Flush(flushToDisk: false);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or ArgumentException
            )
        {
            return new DoctorTemporaryFileProbeResult(
                false,
                "The directory could not be written with an exclusive temporary probe."
            );
        }
        finally
        {
            try
            {
                probe?.Dispose();
            }
            catch (IOException)
            {
                // The post-dispose existence check reports cleanup failure.
            }
        }

        return fileSystem.File.Exists(probePath)
            ? new DoctorTemporaryFileProbeResult(false, "The temporary probe could not be removed.")
            : new DoctorTemporaryFileProbeResult(true, "Writable; temporary probe removed.");
    }

    private string? ValidateDirectoryBoundary(string directory)
    {
        if (!fileSystem.Path.IsPathRooted(directory))
        {
            return "The directory path is not absolute.";
        }

        if (fileSystem.File.Exists(directory))
        {
            return "The expected directory is a file.";
        }

        if (!fileSystem.Directory.Exists(directory))
        {
            return "The directory does not exist.";
        }

        var root = fileSystem.Path.GetPathRoot(directory);
        if (string.IsNullOrEmpty(root))
        {
            return "The directory root could not be identified.";
        }

        try
        {
            var current = root;
            if (IsReparsePoint(current))
            {
                return "The directory path crosses a link or reparse point.";
            }

            var relative = directory[root.Length..];
            foreach (
                var segment in relative.Split(
                    [
                        fileSystem.Path.DirectorySeparatorChar,
                        fileSystem.Path.AltDirectorySeparatorChar,
                    ],
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                current = fileSystem.Path.Combine(current, segment);
                if (IsReparsePoint(current))
                {
                    return "The directory path crosses a link or reparse point.";
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "The directory path could not be inspected safely.";
        }

        return null;
    }

    private bool IsReparsePoint(string path) =>
        fileSystem.Directory.Exists(path)
        && fileSystem.File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
