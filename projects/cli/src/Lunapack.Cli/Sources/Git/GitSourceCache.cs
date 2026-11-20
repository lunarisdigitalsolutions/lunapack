using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Catalog;
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
        ProjectConfiguration.GitSource source,
        ResourceLimits? resourceLimits = null
    )
    {
        var limits = resourceLimits ?? ResourceLimits.Default;
        var identity = GitSourceCacheIdentity.Create(source);
        var cachePath = GetCachePath(projectDirectory, identity);
        if (!fileSystem.File.Exists(cachePath))
        {
            return ManifestOperationResult<GitSourceCacheEntry?>.Success(null);
        }

        try
        {
            var read = BoundedFileReader.ReadBytes(
                fileSystem,
                cachePath,
                limits.LockFileBytes,
                "Git source cache"
            );
            if (read.Value is not { } bytes)
            {
                return ManifestOperationResult<GitSourceCacheEntry?>.Failure(
                    read.Error!,
                    read.ErrorKind
                );
            }

            var content = Encoding.UTF8.GetString(bytes);
            var entry = JsonSerializer.Deserialize(
                content,
                LunapackJsonContext.Default.GitSourceCacheEntry
            );
            return entry is null || !IsValidEntry(projectDirectory, identity, entry, limits)
                ? ManifestOperationResult<GitSourceCacheEntry?>.Success(null)
                : ManifestOperationResult<GitSourceCacheEntry?>.Success(entry);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return ManifestOperationResult<GitSourceCacheEntry?>.Success(null);
        }
    }

    public ManifestOperationResult<bool> Save(
        string projectDirectory,
        GitSourceCacheEntry entry,
        ResourceLimits? resourceLimits = null
    )
    {
        var limits = resourceLimits ?? ResourceLimits.Default;
        var accountedEntry = AccountEntry(entry);
        if (accountedEntry.Packs.Count > limits.FilesPerGraph)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Git source cache contains at least {accountedEntry.Packs.Count} packs, exceeding its {limits.FilesPerGraph} pack-record limit.",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var manifestBudget = new CatalogManifestBudget(limits);
        foreach (var pack in accountedEntry.Packs)
        {
            if (pack.ManifestByteCount > limits.PackManifestBytes)
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Git source cache manifest '{pack.PackPath}' exceeds its {limits.PackManifestBytes} byte limit (at least {pack.ManifestByteCount} bytes measured).",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }

            var observed = manifestBudget.Observe(pack.ManifestByteCount);
            if (!observed.IsSuccess)
            {
                return ManifestOperationResult<bool>.Failure(observed.Error!, observed.ErrorKind);
            }
        }

        var cacheDirectory = GetCacheDirectory(projectDirectory);
        var cachePath = GetCachePath(projectDirectory, accountedEntry.Source);
        var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var content = JsonSerializer.Serialize(
                accountedEntry,
                LunapackJsonContext.Default.GitSourceCacheEntry
            );
            var byteCount = Encoding.UTF8.GetByteCount(content);
            if (byteCount > limits.LockFileBytes)
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Git source cache exceeds its {limits.LockFileBytes} byte limit (at least {byteCount} bytes produced).",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }

            fileSystem.Directory.CreateDirectory(cacheDirectory);
            fileSystem.File.WriteAllText(temporaryPath, content);
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

    public static bool AreEquivalent(GitSourceCacheEntry left, GitSourceCacheEntry right)
    {
        var leftNode = JsonNode.Parse(
            JsonSerializer.Serialize(
                AccountEntry(left),
                LunapackJsonContext.Default.GitSourceCacheEntry
            )
        );
        var rightNode = JsonNode.Parse(
            JsonSerializer.Serialize(
                AccountEntry(right),
                LunapackJsonContext.Default.GitSourceCacheEntry
            )
        );
        return JsonNode.DeepEquals(leftNode, rightNode);
    }

    private string GetCacheDirectory(string projectDirectory) =>
        fileSystem.Path.Combine(projectDirectory, ".lunapack", "git-sources");

    private bool IsValidEntry(
        string projectDirectory,
        GitSourceCacheIdentity identity,
        GitSourceCacheEntry entry,
        ResourceLimits resourceLimits
    ) =>
        entry.Version == CacheVersion
        && entry.Source is not null
        && entry.Packs is not null
        && entry.Packs.Count <= resourceLimits.FilesPerGraph
        && GitRefResolver.IsCommit(entry.ResolvedCommit)
        && string.Equals(entry.Source.Fingerprint, identity.Fingerprint, StringComparison.Ordinal)
        && ArePacksValid(projectDirectory, entry.Packs, resourceLimits);

    private bool ArePacksValid(
        string projectDirectory,
        IReadOnlyList<GitCachedPack> packs,
        ResourceLimits resourceLimits
    )
    {
        var manifestBudget = new CatalogManifestBudget(resourceLimits);
        foreach (var pack in packs)
        {
            if (!IsValidPack(projectDirectory, pack, resourceLimits))
            {
                return false;
            }

            if (!manifestBudget.Observe(pack.ManifestByteCount).IsSuccess)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsValidPack(
        string projectDirectory,
        GitCachedPack? pack,
        ResourceLimits resourceLimits
    )
    {
        if (
            pack?.Manifest is null
            || string.IsNullOrEmpty(pack.Id)
            || string.IsNullOrEmpty(pack.Version)
            || string.IsNullOrEmpty(pack.Manifest.Id)
            || string.IsNullOrEmpty(pack.Manifest.Version)
            || !string.Equals(pack.Id, pack.Manifest.Id, StringComparison.Ordinal)
            || !string.Equals(pack.Version, pack.Manifest.Version, StringComparison.Ordinal)
            || pack.ManifestByteCount <= 0
            || pack.ManifestByteCount > resourceLimits.PackManifestBytes
            || pack.ManifestByteCount != MeasureManifest(pack.Manifest)
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

    private static GitSourceCacheEntry AccountEntry(GitSourceCacheEntry entry) =>
        entry with
        {
            Packs =
            [
                .. entry.Packs.Select(pack =>
                    pack with
                    {
                        ManifestByteCount = MeasureManifest(pack.Manifest),
                    }
                ),
            ],
        };

    private static long MeasureManifest(PackManifest manifest) =>
        JsonSerializer
            .SerializeToUtf8Bytes(manifest, LunapackJsonContext.Default.PackManifest)
            .LongLength;

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
