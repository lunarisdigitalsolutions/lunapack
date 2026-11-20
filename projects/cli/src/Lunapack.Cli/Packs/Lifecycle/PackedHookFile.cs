using System.IO.Abstractions;
using System.Security.Cryptography;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Catalog;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record PackedHookFile(
    string RelativePath,
    string CanonicalPath,
    string Sha256,
    long ByteLimit
)
{
    public PackedHookFile(string relativePath, string canonicalPath, string sha256)
        : this(relativePath, canonicalPath, sha256, ResourceLimits.Default.PackDocumentBytes) { }

    public static ManifestOperationResult<PackedHookFile> Resolve(
        IFileSystem fileSystem,
        DiscoveredPack pack,
        string? packRelativePath,
        ResourceLimits? resourceLimits = null
    )
    {
        if (string.IsNullOrEmpty(packRelativePath))
        {
            return ManifestOperationResult<PackedHookFile>.Failure(
                "Packed lifecycle hook files must specify a relative file path."
            );
        }

        var packDirectory = fileSystem.Path.GetFullPath(pack.PackDirectory);
        var normalizedPath = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            packDirectory,
            packRelativePath
        );
        if (normalizedPath.Value is not { } relativePath)
        {
            return ManifestOperationResult<PackedHookFile>.Failure(
                normalizedPath.Error ?? "Packed lifecycle hook file path is invalid."
            );
        }

        var snapshotRoot = fileSystem.Path.GetFullPath(pack.SourcePath);
        var canonicalPath = fileSystem.Path.GetFullPath(relativePath, packDirectory);
        if (!IsWithinSnapshot(fileSystem, snapshotRoot, canonicalPath))
        {
            return ManifestOperationResult<PackedHookFile>.Failure(
                $"Packed lifecycle hook file '{relativePath}' must resolve within its snapshot."
            );
        }

        if (!fileSystem.File.Exists(canonicalPath))
        {
            return ManifestOperationResult<PackedHookFile>.Failure(
                $"Packed lifecycle hook file '{relativePath}' does not exist in its snapshot."
            );
        }

        var byteLimit = (resourceLimits ?? ResourceLimits.Default).PackDocumentBytes;
        var digest = ComputeSha256(
            fileSystem,
            canonicalPath,
            byteLimit,
            $"Lifecycle hook file '{relativePath}'"
        );
        return digest.Value is { } sha256
            ? ManifestOperationResult<PackedHookFile>.Success(
                new PackedHookFile(relativePath, canonicalPath, sha256, byteLimit)
            )
            : ManifestOperationResult<PackedHookFile>.Failure(
                digest.Error ?? $"Unable to read lifecycle hook file '{relativePath}'.",
                digest.ErrorKind
            );
    }

    public ManifestOperationResult<bool> Verify(IFileSystem fileSystem)
    {
        if (!fileSystem.File.Exists(CanonicalPath))
        {
            return ManifestOperationResult<bool>.Failure(
                $"Packed lifecycle hook file '{RelativePath}' no longer exists in its snapshot."
            );
        }

        var digest = ComputeSha256(
            fileSystem,
            CanonicalPath,
            ByteLimit,
            $"Lifecycle hook file '{RelativePath}'"
        );
        if (digest.Value is not { } sha256)
        {
            return ManifestOperationResult<bool>.Failure(
                digest.Error ?? $"Unable to verify lifecycle hook file '{RelativePath}'.",
                digest.ErrorKind
            );
        }

        return string.Equals(sha256, Sha256, StringComparison.Ordinal)
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(
                $"Packed lifecycle hook file '{RelativePath}' changed after authorization."
            );
    }

    private static ManifestOperationResult<string> ComputeSha256(
        IFileSystem fileSystem,
        string path,
        long byteLimit,
        string inputName
    )
    {
        try
        {
            using var stream = fileSystem.File.OpenRead(path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81_920];
            long bytesRead = 0;
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                var observedBytes = checked(bytesRead + read);
                if (observedBytes > byteLimit)
                {
                    return ManifestOperationResult<string>.Failure(
                        $"{inputName} exceeds its {byteLimit} byte limit (at least {observedBytes} bytes read).",
                        ManifestOperationErrorKind.ResourceLimitExceeded
                    );
                }

                hash.AppendData(buffer, 0, read);
                bytesRead = observedBytes;
            }

            return ManifestOperationResult<string>.Success(
                Convert.ToHexString(hash.GetHashAndReset())
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<string>.Failure(
                $"Unable to read {inputName}: {exception.Message}"
            );
        }
    }

    private static bool IsWithinSnapshot(IFileSystem fileSystem, string snapshotRoot, string path)
    {
        var comparison =
            fileSystem.Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        var rootWithSeparator = snapshotRoot.EndsWith(
            fileSystem.Path.DirectorySeparatorChar.ToString(),
            comparison
        )
            ? snapshotRoot
            : $"{snapshotRoot}{fileSystem.Path.DirectorySeparatorChar}";
        return path.StartsWith(rootWithSeparator, comparison);
    }
}
