using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunapack.Cli;

internal sealed class GitSourceCache(IFileSystem fileSystem)
{
    public const int CacheVersion = 1;

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ManifestOperationResult<GitSourceCacheEntry?> Load(
        string projectDirectory,
        ProjectConfiguration.GitSource source
    )
    {
        var identity = GitSourceCacheIdentity.Create(source);
        var cachePath = GetCachePath(projectDirectory, identity);
        if (!fileSystem.File.Exists(cachePath))
        {
            return ManifestOperationResult<GitSourceCacheEntry?>.Success(null);
        }

        try
        {
            var content = fileSystem.File.ReadAllText(cachePath);
            var entry = JsonSerializer.Deserialize(
                content,
                LunapackJsonContext.Default.GitSourceCacheEntry
            );
            return entry is null || entry.Version != CacheVersion || entry.Source != identity
                ? ManifestOperationResult<GitSourceCacheEntry?>.Success(null)
                : ManifestOperationResult<GitSourceCacheEntry?>.Success(entry);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return ManifestOperationResult<GitSourceCacheEntry?>.Success(null);
        }
    }

    public ManifestOperationResult<bool> Save(string projectDirectory, GitSourceCacheEntry entry)
    {
        var cachePath = GetCachePath(projectDirectory, entry.Source);
        var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(cachePath)!);
            fileSystem.File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(entry, LunapackJsonContext.Default.GitSourceCacheEntry)
            );
            fileSystem.File.Move(temporaryPath, cachePath, overwrite: true);
            return ManifestOperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Unable to persist Git source cache: {exception.Message}"
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

    private string GetCachePath(string projectDirectory, GitSourceCacheIdentity identity) =>
        fileSystem.Path.Combine(
            projectDirectory,
            ".lunapack",
            "git-sources",
            $"{GetFileName(identity)}.json"
        );

    private static string GetFileName(GitSourceCacheIdentity identity)
    {
        var cacheKey = string.Join('\n', identity.Url, identity.Ref, identity.Path);
        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))
            .ToLowerInvariant();
    }
}
