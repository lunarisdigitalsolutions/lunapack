using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using NuGet.Versioning;

namespace Lunapack.Cli.Sources.Git;

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
            return entry is null || !IsValidEntry(projectDirectory, identity, entry)
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
        var cacheDirectory = GetCacheDirectory(projectDirectory);
        var cachePath = GetCachePath(projectDirectory, entry.Source);
        var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            fileSystem.Directory.CreateDirectory(cacheDirectory);
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

    private string GetCacheDirectory(string projectDirectory) =>
        fileSystem.Path.Combine(projectDirectory, ".lunapack", "git-sources");

    private bool IsValidEntry(
        string projectDirectory,
        GitSourceCacheIdentity identity,
        GitSourceCacheEntry entry
    ) =>
        entry.Version == CacheVersion
        && entry.Source is not null
        && entry.Packs is not null
        && GitRefResolver.IsCommit(entry.ResolvedCommit)
        && string.Equals(entry.Source.Fingerprint, identity.Fingerprint, StringComparison.Ordinal)
        && entry.Packs.All(pack => IsValidPack(projectDirectory, pack));

    private bool IsValidPack(string projectDirectory, GitCachedPack? pack)
    {
        if (
            pack?.Manifest is null
            || string.IsNullOrEmpty(pack.Id)
            || string.IsNullOrEmpty(pack.Version)
            || string.IsNullOrEmpty(pack.Manifest.Id)
            || string.IsNullOrEmpty(pack.Manifest.Version)
            || !string.Equals(pack.Id, pack.Manifest.Id, StringComparison.Ordinal)
            || !string.Equals(pack.Version, pack.Manifest.Version, StringComparison.Ordinal)
            || !NuGetVersion.TryParse(pack.Version, out _)
            || ManifestModelValidator.Validate(pack.Manifest).Count > 0
        )
        {
            return false;
        }

        var normalizedPath = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            pack.PackPath
        );
        return normalizedPath.IsSuccess
            && string.Equals(normalizedPath.Value, pack.PackPath, StringComparison.Ordinal);
    }

    private string GetCachePath(string projectDirectory, GitSourceCacheIdentity identity) =>
        fileSystem.Path.Combine(
            GetCacheDirectory(projectDirectory),
            $"{GetFileName(identity)}.json"
        );

    private static string GetFileName(GitSourceCacheIdentity identity) =>
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Fingerprint)))
            .ToLowerInvariant();
}
