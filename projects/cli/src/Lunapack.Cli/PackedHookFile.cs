using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Lunapack.Cli;

internal sealed record PackedHookFile(string RelativePath, string CanonicalPath, string Sha256)
{
    public static ManifestOperationResult<PackedHookFile> Resolve(
        IFileSystem fileSystem,
        DiscoveredPack pack,
        string? packRelativePath
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

        return ManifestOperationResult<PackedHookFile>.Success(
            new PackedHookFile(
                relativePath,
                canonicalPath,
                ComputeSha256(fileSystem, canonicalPath)
            )
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

        try
        {
            return string.Equals(
                ComputeSha256(fileSystem, CanonicalPath),
                Sha256,
                StringComparison.Ordinal
            )
                ? ManifestOperationResult<bool>.Success(true)
                : ManifestOperationResult<bool>.Failure(
                    $"Packed lifecycle hook file '{RelativePath}' changed after authorization."
                );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Unable to verify packed lifecycle hook file '{RelativePath}': {exception.Message}"
            );
        }
    }

    private static string ComputeSha256(IFileSystem fileSystem, string path)
    {
        using var stream = fileSystem.File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
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
