using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Links;

internal sealed class GitLinkCache(IFileSystem fileSystem, string cacheRoot)
{
    public const int CacheVersion = 1;

    public GitLinkCacheMetadata? LoadMetadata(ConfiguredSourceIdentity identity, string commit)
    {
        var metadataPath = GetMetadataPath(identity, commit);
        if (!fileSystem.File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize(
                fileSystem.File.ReadAllText(metadataPath),
                LunapackJsonContext.Default.GitLinkCacheMetadata
            );
            return
                metadata is null
                || metadata.Version != CacheVersion
                || metadata.Source != identity
                || !string.Equals(metadata.ResolvedCommit, commit, StringComparison.Ordinal)
                ? null
                : metadata;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public ManifestOperationResult<bool> SaveMetadata(GitLinkCacheMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return WriteAtomically(
            GetMetadataPath(metadata.Source, metadata.ResolvedCommit),
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(metadata, LunapackJsonContext.Default.GitLinkCacheMetadata)
            )
        );
    }

    public byte[]? TryReadBlob(ConfiguredSourceIdentity identity, string commit, string blobId)
    {
        var blobPath = GetBlobPath(identity, commit, blobId);
        if (!fileSystem.File.Exists(blobPath))
        {
            return null;
        }

        try
        {
            var contents = fileSystem.File.ReadAllBytes(blobPath);
            if (GitObjectId.Matches(blobId, contents))
            {
                return contents;
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
