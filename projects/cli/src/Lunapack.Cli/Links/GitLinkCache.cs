using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Links;

internal sealed class GitLinkCache(IFileSystem fileSystem, string cacheRoot)
{
    public const int CacheVersion = 2;

    public GitLinkCacheMetadata? LoadMetadata(
        ConfiguredSourceIdentity identity,
        string commit,
        ResourceLimits? resourceLimits = null
    )
    {
        var metadataPath = GetMetadataPath(identity, commit);
        if (!fileSystem.File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var limits = resourceLimits ?? ResourceLimits.Default;
            var read = BoundedFileReader.ReadBytes(
                fileSystem,
                metadataPath,
                limits.LockFileBytes,
                "Git link cache metadata"
            );
            if (read.Value is not { } bytes)
            {
                return null;
            }

            var metadata = JsonSerializer.Deserialize(
                bytes,
                LunapackJsonContext.Default.GitLinkCacheMetadata
            );
            return
                metadata is null
                || metadata.Version != CacheVersion
                || metadata.Source != identity
                || !string.Equals(metadata.ResolvedCommit, commit, StringComparison.Ordinal)
                || metadata.Tree.Count > limits.FilesPerGraph
                ? null
                : metadata;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public ManifestOperationResult<bool> SaveMetadata(
        GitLinkCacheMetadata metadata,
        ResourceLimits? resourceLimits = null
    )
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var limits = resourceLimits ?? ResourceLimits.Default;
        if (metadata.Tree.Count > limits.FilesPerGraph)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Git link cache contains at least {metadata.Tree.Count} files, exceeding its {limits.FilesPerGraph} file limit.",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var contents = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(metadata, LunapackJsonContext.Default.GitLinkCacheMetadata)
        );
        return contents.LongLength > limits.LockFileBytes
            ? ManifestOperationResult<bool>.Failure(
                $"Git link cache metadata exceeds its {limits.LockFileBytes} byte limit (at least {contents.LongLength} bytes produced).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            )
            : WriteAtomically(GetMetadataPath(metadata.Source, metadata.ResolvedCommit), contents);
    }

    public byte[]? TryReadBlob(ConfiguredSourceIdentity identity, string commit, string blobId)
    {
        var blobPath = TryGetBlobPath(identity, commit, blobId, long.MaxValue);
        return blobPath is null ? null : fileSystem.File.ReadAllBytes(blobPath);
    }

    public string? TryGetBlobPath(
        ConfiguredSourceIdentity identity,
        string commit,
        string blobId,
        long maximumBytes
    )
    {
        var blobPath = GetBlobPath(identity, commit, blobId);
        if (
            !fileSystem.File.Exists(blobPath)
            || fileSystem.FileInfo.New(blobPath).Length > maximumBytes
        )
        {
            return null;
        }

        try
        {
            using var contents = fileSystem.File.OpenRead(blobPath);
            if (GitObjectId.Matches(blobId, contents, contents.Length))
            {
                return blobPath;
            }

            fileSystem.File.Delete(blobPath);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public ManifestOperationResult<bool> SaveBlob(
        ConfiguredSourceIdentity identity,
        string commit,
        string blobId,
        byte[] contents
    ) => WriteAtomically(GetBlobPath(identity, commit, blobId), contents);

    public ManifestOperationResult<bool> SaveBlobFromPath(
        ConfiguredSourceIdentity identity,
        string commit,
        string blobId,
        string sourcePath
    ) => CopyAtomically(GetBlobPath(identity, commit, blobId), sourcePath);

    private ManifestOperationResult<bool> WriteAtomically(string path, byte[] contents)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (fileSystem.Path.GetDirectoryName(path) is not { Length: > 0 } directory)
            {
                throw new InvalidOperationException(
                    $"Git link cache path '{path}' does not have a directory."
                );
            }

            fileSystem.Directory.CreateDirectory(directory);
            fileSystem.File.WriteAllBytes(temporaryPath, contents);
            fileSystem.File.Move(temporaryPath, path, overwrite: true);
            return ManifestOperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Unable to persist Git link cache content: {exception.Message}"
            );
        }
        finally
        {
            if (fileSystem.File.Exists(temporaryPath))
            {
                fileSystem.File.Delete(temporaryPath);
            }
        }
    }

    private ManifestOperationResult<bool> CopyAtomically(string path, string sourcePath)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (fileSystem.Path.GetDirectoryName(path) is not { Length: > 0 } directory)
            {
                throw new InvalidOperationException(
                    $"Git link cache path '{path}' does not have a directory."
                );
            }

            fileSystem.Directory.CreateDirectory(directory);
            fileSystem.File.Copy(sourcePath, temporaryPath, overwrite: true);
            fileSystem.File.Move(temporaryPath, path, overwrite: true);
            return ManifestOperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Unable to persist Git link cache content: {exception.Message}"
            );
        }
        finally
        {
            if (fileSystem.File.Exists(temporaryPath))
            {
                fileSystem.File.Delete(temporaryPath);
            }
        }
    }

    private string GetMetadataPath(ConfiguredSourceIdentity identity, string commit) =>
        fileSystem.Path.Combine(GetCommitDirectory(identity, commit), "metadata.json");

    private string GetBlobPath(ConfiguredSourceIdentity identity, string commit, string blobId) =>
        fileSystem.Path.Combine(GetCommitDirectory(identity, commit), "blobs", blobId);

    private string GetCommitDirectory(ConfiguredSourceIdentity identity, string commit) =>
        fileSystem.Path.Combine(cacheRoot, CreateSourceKey(identity), commit);

    private static string CreateSourceKey(ConfiguredSourceIdentity identity) =>
        Convert
            .ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        string.Join(
                            '\u001f',
                            identity.Type,
                            identity.Url ?? string.Empty,
                            identity.Ref ?? string.Empty,
                            identity.Path ?? string.Empty
                        )
                    )
                )
            )
            .ToLowerInvariant();
}
