using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Packs.Manifest;
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
        bool resolveLockedRootPacks = false,
        ResourceLimits? resourceLimits = null
    ) =>
        await ResolveAsync(
            projectDirectory,
            configuration,
            [new ProjectConfiguration.RequestedPack { Id = packId, Version = requestedVersion }],
            lockFile,
            resolveLockedRootPacks,
            resourceLimits
        );

    public async Task<ManifestOperationResult<ResolvedPackGraph>> ResolveAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        IReadOnlyList<ProjectConfiguration.RequestedPack> requestedPacks,
        ProjectLockFile? lockFile = null,
        bool resolveLockedRootPacks = false,
        ResourceLimits? resourceLimits = null
    )
    {
        var effectiveResourceLimits = resourceLimits ?? ResourceLimits.Default;
        var resolvedById = new Dictionary<string, DiscoveredPack>(StringComparer.Ordinal);
        var resolvedPacks = new List<DiscoveredPack>();
        var visiting = new HashSet<PackIdentity>();
        var resolutionCache =
            new Dictionary<PackRequest, ManifestOperationResult<DiscoveredPack>>();
        var traversalBudget = new GraphTraversalBudget(effectiveResourceLimits);
        var catalogBudget = new CatalogManifestBudget(effectiveResourceLimits);
        foreach (var rootRequest in requestedPacks)
        {
            var root = await ResolveRootPackAsync(
                projectDirectory,
                configuration,
                rootRequest,
                lockFile,
                resolveLockedRootPacks,
                effectiveResourceLimits,
                resolutionCache,
                catalogBudget
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
                effectiveResourceLimits,
                resolutionCache,
                traversalBudget,
                catalogBudget,
                depth: 0
            );
            if (!error.IsSuccess)
            {
                return ManifestOperationResult<ResolvedPackGraph>.Failure(
                    error.Error ?? "Unable to resolve composite pack graph.",
                    error.ErrorKind
                );
            }
        }

        var graph = new ResolvedPackGraph(
            resolvedPacks,
            requestedPacks.Select(pack => pack.Id).ToHashSet(StringComparer.Ordinal)
        );
        var depthValidation = ValidateLongestDepth(graph, effectiveResourceLimits);
        return depthValidation.IsSuccess
            ? ManifestOperationResult<ResolvedPackGraph>.Success(graph)
            : ManifestOperationResult<ResolvedPackGraph>.Failure(
                depthValidation.Error!,
                depthValidation.ErrorKind
            );
    }

    private static ManifestOperationResult<bool> ValidateLongestDepth(
        ResolvedPackGraph graph,
        ResourceLimits resourceLimits
    )
    {
        var dependencies = graph.Packs.ToDictionary(
            pack => pack.Manifest.Id,
            pack => pack.Manifest.Packs.Select(reference => reference.Id).ToList(),
            StringComparer.Ordinal
        );
        var indegree = graph.Packs.ToDictionary(
            pack => pack.Manifest.Id,
            _ => 0L,
            StringComparer.Ordinal
        );
        foreach (var dependencyIds in dependencies.Values)
        {
            foreach (var dependencyId in dependencyIds)
            {
                if (indegree.TryGetValue(dependencyId, out var count))
                {
                    indegree[dependencyId] = count + 1;
                }
            }
        }

        var queue = new Queue<string>(
            indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key)
        );
        var order = new List<string>();
        while (queue.TryDequeue(out var packId))
        {
            order.Add(packId);
            foreach (var dependencyId in dependencies[packId])
            {
                indegree[dependencyId]--;
                if (indegree[dependencyId] == 0)
                {
                    queue.Enqueue(dependencyId);
                }
            }
        }

        var depths = graph
            .Packs.Where(graph.IsRoot)
            .ToDictionary(pack => pack.Manifest.Id, _ => 0L, StringComparer.Ordinal);
        foreach (var packId in order)
        {
            if (!depths.TryGetValue(packId, out var depth))
            {
                continue;
            }

            foreach (var dependencyId in dependencies[packId])
            {
                var observedDepth = checked(depth + 1);
                if (observedDepth > resourceLimits.DependencyDepth)
                {
                    return ManifestOperationResult<bool>.Failure(
                        $"Composite pack graph path '{packId}' to '{dependencyId}' exceeds its {resourceLimits.DependencyDepth} edge-depth limit (at least {observedDepth} edges observed).",
                        ManifestOperationErrorKind.ResourceLimitExceeded
                    );
                }

                if (
                    !depths.TryGetValue(dependencyId, out var existingDepth)
                    || observedDepth > existingDepth
                )
                {
                    depths[dependencyId] = observedDepth;
                }
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private async Task<ManifestOperationResult<bool>> ResolveDepthFirstAsync(
        DiscoveredPack pack,
        string projectDirectory,
        ProjectConfiguration configuration,
        IDictionary<string, DiscoveredPack> resolvedById,
        ICollection<DiscoveredPack> resolvedPacks,
        ISet<PackIdentity> visiting,
        ProjectLockFile? lockFile,
        ResourceLimits resourceLimits,
        IDictionary<PackRequest, ManifestOperationResult<DiscoveredPack>> resolutionCache,
        GraphTraversalBudget traversalBudget,
        CatalogManifestBudget catalogBudget,
        long depth
    )
    {
        if (resolvedById.TryGetValue(pack.Manifest.Id, out var resolvedPack))
        {
            return string.Equals(
                resolvedPack.Manifest.Version,
                pack.Manifest.Version,
                StringComparison.Ordinal
            )
                ? ManifestOperationResult<bool>.Success(true)
                : ManifestOperationResult<bool>.Failure(
                    $"Pack '{pack.Manifest.Id}' resolves to conflicting versions '{resolvedPack.Manifest.Version}' and '{pack.Manifest.Version}'."
                );
        }

        var identity = new PackIdentity(pack.Manifest.Id, pack.Manifest.Version);
        if (!visiting.Add(identity))
        {
            return ManifestOperationResult<bool>.Failure(
                $"Composite pack graph contains a cycle at '{identity.Id}@{identity.Version}'."
            );
        }

        try
        {
            var node = traversalBudget.ObserveNode(identity);
            if (!node.IsSuccess)
            {
                return node;
            }

            var references = await ResolveReferencesAsync(
                pack,
                projectDirectory,
                configuration,
                resolvedById,
                resolvedPacks,
                visiting,
                lockFile,
                resourceLimits,
                resolutionCache,
                traversalBudget,
                catalogBudget,
                depth
            );
            if (!references.IsSuccess)
            {
                return references;
            }

            resolvedById.Add(pack.Manifest.Id, pack);
            resolvedPacks.Add(pack);
            return ManifestOperationResult<bool>.Success(true);
        }
        finally
        {
            visiting.Remove(identity);
        }
    }

    private async Task<ManifestOperationResult<bool>> ResolveReferencesAsync(
        DiscoveredPack pack,
        string projectDirectory,
        ProjectConfiguration configuration,
        IDictionary<string, DiscoveredPack> resolvedById,
        ICollection<DiscoveredPack> resolvedPacks,
        ISet<PackIdentity> visiting,
        ProjectLockFile? lockFile,
        ResourceLimits resourceLimits,
        IDictionary<PackRequest, ManifestOperationResult<DiscoveredPack>> resolutionCache,
        GraphTraversalBudget traversalBudget,
        CatalogManifestBudget catalogBudget,
        long depth
    )
    {
        var identity = new PackIdentity(pack.Manifest.Id, pack.Manifest.Version);
        foreach (var reference in pack.Manifest.Packs)
        {
            var traversed = traversalBudget.ObserveReference(identity, reference, depth);
            if (!traversed.IsSuccess)
            {
                return traversed;
            }

            var dependency = await ResolveDependencyPackAsync(
                projectDirectory,
                configuration,
                reference.Id,
                reference.Version,
                lockFile,
                resourceLimits,
                resolutionCache,
                catalogBudget
            );
            if (dependency.Value is not { } dependencyPack)
            {
                return ManifestOperationResult<bool>.Failure(
                    dependency.Error
                        ?? $"Pack '{reference.Id}@{reference.Version}' is unavailable.",
                    dependency.ErrorKind
                );
            }

            var resolved = await ResolveDepthFirstAsync(
                dependencyPack,
                projectDirectory,
                configuration,
                resolvedById,
                resolvedPacks,
                visiting,
                lockFile,
                resourceLimits,
                resolutionCache,
                traversalBudget,
                catalogBudget,
                depth + 1
            );
            if (!resolved.IsSuccess)
            {
                return resolved;
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private sealed class GraphTraversalBudget(ResourceLimits limits)
    {
        private readonly HashSet<PackIdentity> _nodes = [];
        private long _references;

        public ManifestOperationResult<bool> ObserveNode(PackIdentity identity)
        {
            if (!_nodes.Add(identity))
            {
                return ManifestOperationResult<bool>.Success(true);
            }

            return _nodes.Count > limits.DependencyNodes
                ? ManifestOperationResult<bool>.Failure(
                    $"Composite pack graph exceeds its {limits.DependencyNodes} node limit (at least {_nodes.Count} unique nodes observed).",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                )
                : ManifestOperationResult<bool>.Success(true);
        }

        public ManifestOperationResult<bool> ObserveReference(
            PackIdentity source,
            PackManifest.PackReference reference,
            long currentDepth
        )
        {
            var observedReferences = checked(_references + 1);
            _references = observedReferences;
            if (observedReferences > limits.DependencyReferences)
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Composite pack graph exceeds its {limits.DependencyReferences} reference limit (at least {observedReferences} references traversed).",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }

            var observedDepth = checked(currentDepth + 1);
            return observedDepth > limits.DependencyDepth
                ? ManifestOperationResult<bool>.Failure(
                    $"Composite pack graph reference '{source.Id}@{source.Version}' to '{reference.Id}@{reference.Version}' exceeds its {limits.DependencyDepth} edge-depth limit (at least {observedDepth} edges observed).",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                )
                : ManifestOperationResult<bool>.Success(true);
        }
    }

    private async Task<ManifestOperationResult<DiscoveredPack>> ResolveRootPackAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ProjectConfiguration.RequestedPack rootRequest,
        ProjectLockFile? lockFile,
        bool resolveLockedRootPacks,
        ResourceLimits resourceLimits,
        IDictionary<PackRequest, ManifestOperationResult<DiscoveredPack>> resolutionCache,
        CatalogManifestBudget catalogBudget
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
                ? await packCatalog.ResolveLockedAsync(
                    projectDirectory,
                    configuration,
                    lockedPack,
                    resourceLimits,
                    catalogBudget
                )
                : await packCatalog.ResolveDirectlyInstallableAsync(
                    projectDirectory,
                    configuration,
                    rootRequest.Id,
                    rootRequest.Version,
                    resourceLimits,
                    catalogBudget
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
        ResourceLimits resourceLimits,
        IDictionary<PackRequest, ManifestOperationResult<DiscoveredPack>> resolutionCache,
        CatalogManifestBudget catalogBudget
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
                ? await packCatalog.ResolveLockedAsync(
                    projectDirectory,
                    configuration,
                    lockedPack,
                    resourceLimits,
                    catalogBudget
                )
                : await packCatalog.ResolveAsync(
                    projectDirectory,
                    configuration,
                    packId,
                    requestedVersion,
                    resourceLimits,
                    catalogBudget
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
