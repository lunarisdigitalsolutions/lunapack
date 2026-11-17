using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Catalog;

internal sealed class CompositePackGraphResolver(PackCatalog packCatalog)
{
    public async Task<ManifestOperationResult<ResolvedPackGraph>> ResolveAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string packId,
        string? requestedVersion,
        ProjectLockFile? lockFile = null,
        bool resolveLockedRootPacks = false
    ) =>
        await ResolveAsync(
            projectDirectory,
            configuration,
            [new ProjectConfiguration.RequestedPack { Id = packId, Version = requestedVersion }],
            lockFile,
            resolveLockedRootPacks
        );

    public async Task<ManifestOperationResult<ResolvedPackGraph>> ResolveAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        IReadOnlyList<ProjectConfiguration.RequestedPack> requestedPacks,
        ProjectLockFile? lockFile = null,
        bool resolveLockedRootPacks = false
    )
    {
        var resolvedById = new Dictionary<string, DiscoveredPack>(StringComparer.Ordinal);
        var resolvedPacks = new List<DiscoveredPack>();
        var visiting = new HashSet<PackIdentity>();
        var resolutionCache =
            new Dictionary<PackRequest, ManifestOperationResult<DiscoveredPack>>();
        foreach (var rootRequest in requestedPacks)
        {
            var root = await ResolveRootPackAsync(
                projectDirectory,
                configuration,
                rootRequest,
                lockFile,
                resolveLockedRootPacks,
                resolutionCache
            );
            if (root.Value is not { } rootPack)
            {
                return ManifestOperationResult<ResolvedPackGraph>.Failure(
                    root.Error ?? $"Pack '{rootRequest.Id}' is unavailable.",
                    root.ErrorKind
                );
            }

            if (!rootPack.Manifest.DirectInstallable)
            {
                return ManifestOperationResult<ResolvedPackGraph>.Failure(
                    $"Pack '{rootRequest.Id}' is not directly installable (directInstallable is false).",
                    ManifestOperationErrorKind.PackNotFound
                );
            }

            var error = await ResolveDepthFirstAsync(
                rootPack,
                projectDirectory,
                configuration,
                resolvedById,
                resolvedPacks,
                visiting,
                lockFile,
                resolutionCache
            );
            if (error is not null)
            {
                return ManifestOperationResult<ResolvedPackGraph>.Failure(error);
            }
        }

        return ManifestOperationResult<ResolvedPackGraph>.Success(
            new ResolvedPackGraph(
                resolvedPacks,
                requestedPacks.Select(pack => pack.Id).ToHashSet(StringComparer.Ordinal)
            )
        );
    }

    private async Task<string?> ResolveDepthFirstAsync(
        DiscoveredPack pack,
        string projectDirectory,
        ProjectConfiguration configuration,
        IDictionary<string, DiscoveredPack> resolvedById,
        ICollection<DiscoveredPack> resolvedPacks,
        ISet<PackIdentity> visiting,
        ProjectLockFile? lockFile,
        IDictionary<PackRequest, ManifestOperationResult<DiscoveredPack>> resolutionCache
    )
    {
        if (resolvedById.TryGetValue(pack.Manifest.Id, out var resolvedPack))
        {
            return string.Equals(
                resolvedPack.Manifest.Version,
                pack.Manifest.Version,
                StringComparison.Ordinal
            )
                ? null
                : $"Pack '{pack.Manifest.Id}' resolves to conflicting versions '{resolvedPack.Manifest.Version}' and '{pack.Manifest.Version}'.";
        }

        var identity = new PackIdentity(pack.Manifest.Id, pack.Manifest.Version);
        if (!visiting.Add(identity))
        {
            return $"Composite pack graph contains a cycle at '{identity.Id}@{identity.Version}'.";
        }

        foreach (var reference in pack.Manifest.Packs)
        {
            var dependency = await ResolveDependencyPackAsync(
                projectDirectory,
                configuration,
                reference.Id,
                reference.Version,
                lockFile,
                resolutionCache
            );
            if (dependency.Value is not { } dependencyPack)
            {
                return dependency.Error
                    ?? $"Pack '{reference.Id}@{reference.Version}' is unavailable.";
            }

            var error = await ResolveDepthFirstAsync(
                dependencyPack,
                projectDirectory,
                configuration,
                resolvedById,
                resolvedPacks,
                visiting,
                lockFile,
                resolutionCache
            );
            if (error is not null)
            {
                return error;
            }
        }

        visiting.Remove(identity);
        resolvedById.Add(pack.Manifest.Id, pack);
        resolvedPacks.Add(pack);

        return null;
    }

    private async Task<ManifestOperationResult<DiscoveredPack>> ResolveRootPackAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ProjectConfiguration.RequestedPack rootRequest,
        ProjectLockFile? lockFile,
        bool resolveLockedRootPacks,
        IDictionary<PackRequest, ManifestOperationResult<DiscoveredPack>> resolutionCache
    )
    {
        var key = new PackRequest(
            rootRequest.Id,
            rootRequest.Version,
            PackResolutionRole.DirectRoot
        );
        if (resolutionCache.TryGetValue(key, out var cachedResolution))
        {
            return cachedResolution;
        }

        var lockedPack = resolveLockedRootPacks
            ? FindLockedPack(lockFile, rootRequest.Id, rootRequest.Version)
            : null;
        var resolution =
            lockedPack is not null
            && (lockedPack.SourceIdentity is not null || lockedPack.OciSource is not null)
                ? await packCatalog.ResolveLockedAsync(projectDirectory, configuration, lockedPack)
                : await packCatalog.ResolveDirectlyInstallableAsync(
                    projectDirectory,
                    configuration,
                    rootRequest.Id,
                    rootRequest.Version
                );
        resolutionCache.Add(key, resolution);
        return resolution;
    }

    private async Task<ManifestOperationResult<DiscoveredPack>> ResolveDependencyPackAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string packId,
        string? requestedVersion,
        ProjectLockFile? lockFile,
        IDictionary<PackRequest, ManifestOperationResult<DiscoveredPack>> resolutionCache
    )
    {
        var key = new PackRequest(packId, requestedVersion, PackResolutionRole.Dependency);
        if (resolutionCache.TryGetValue(key, out var cachedResolution))
        {
            return cachedResolution;
        }

        var lockedPack = FindLockedPack(lockFile, packId, requestedVersion);
        var resolution =
            lockedPack is not null
            && (lockedPack.SourceIdentity is not null || lockedPack.OciSource is not null)
                ? await packCatalog.ResolveLockedAsync(projectDirectory, configuration, lockedPack)
                : await packCatalog.ResolveAsync(
                    projectDirectory,
                    configuration,
                    packId,
                    requestedVersion
                );
        resolutionCache.Add(key, resolution);
        return resolution;
    }

    private static ProjectLockFile.ResolvedPack? FindLockedPack(
        ProjectLockFile? lockFile,
        string packId,
        string? requestedVersion
    ) =>
        requestedVersion is null
            ? null
            : lockFile?.Packs.Find(pack =>
                string.Equals(pack.Id, packId, StringComparison.Ordinal)
                && string.Equals(pack.Version, requestedVersion, StringComparison.Ordinal)
            );

    private enum PackResolutionRole
    {
        DirectRoot,
        Dependency,
    }

    private sealed record PackIdentity(string Id, string Version);

    private sealed record PackRequest(string Id, string? Version, PackResolutionRole Role);
}
