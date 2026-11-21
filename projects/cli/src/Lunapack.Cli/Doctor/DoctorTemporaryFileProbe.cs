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
        return ProbeTemporaryFile(probePath);
    }

    private DoctorTemporaryFileProbeResult ProbeTemporaryFile(string probePath)
    {
        try
        {
            using var probe = fileSystem.File.Open(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );
            probe.WriteByte(0);
            probe.Flush();
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or ArgumentException
            )
        {
            return RemoveProbeAfterWriteFailure(probePath);
        }

        return RemoveProbe(probePath);
    }

    private DoctorTemporaryFileProbeResult RemoveProbeAfterWriteFailure(string probePath)
    {
        try
        {
            fileSystem.File.Delete(probePath);
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
                $"The directory could not be written with an exclusive temporary probe. Temporary probe cleanup also failed: {exception.Message}"
            );
        }

        return new DoctorTemporaryFileProbeResult(
            false,
            "The directory could not be written with an exclusive temporary probe."
        );
    }

    private DoctorTemporaryFileProbeResult RemoveProbe(string probePath)
    {
        try
        {
            fileSystem.File.Delete(probePath);
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
                $"The temporary probe could not be removed: {exception.Message}"
            );
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
            var tempDirectory = fileSystem.Path.TrimEndingDirectorySeparator(
                fileSystem.Path.GetFullPath(Path.GetTempPath())
            );
            var tempRelative = fileSystem.Path.GetRelativePath(tempDirectory, directory);
            var isWithinTempDirectory = IsWithinDirectory(tempRelative);
            var validationStart = isWithinTempDirectory ? tempDirectory : root;
            var relative = isWithinTempDirectory
                ? string.Equals(tempRelative, ".", StringComparison.Ordinal)
                    ? ""
                    : tempRelative
                : directory[root.Length..];

            var current = validationStart;
            if (!isWithinTempDirectory && IsReparsePoint(current))
            {
                return "The directory path crosses a link or reparse point.";
            }

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

    private bool IsWithinDirectory(string relativePath)
    {
        if (fileSystem.Path.IsPathRooted(relativePath))
        {
            return false;
        }

        if (string.Equals(relativePath, "..", StringComparison.Ordinal))
        {
            return false;
        }

        var directorySeparator = fileSystem.Path.DirectorySeparatorChar;
        var altDirectorySeparator = fileSystem.Path.AltDirectorySeparatorChar;
        return !relativePath.StartsWith($"..{directorySeparator}", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{altDirectorySeparator}", StringComparison.Ordinal);
    }

    private bool IsReparsePoint(string path) =>
        fileSystem.Directory.Exists(path)
        && fileSystem.File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
