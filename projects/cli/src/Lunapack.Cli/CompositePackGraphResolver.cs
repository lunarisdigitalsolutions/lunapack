namespace Lunapack.Cli;

internal sealed class CompositePackGraphResolver(PackCatalog packCatalog)
{
    public async Task<ManifestOperationResult<ResolvedPackGraph>> ResolveAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string packId,
        string? requestedVersion
    ) =>
        await ResolveAsync(
            projectDirectory,
            configuration,
            [new ProjectConfiguration.RequestedPack { Id = packId, Version = requestedVersion }]
        );

    public async Task<ManifestOperationResult<ResolvedPackGraph>> ResolveAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        IReadOnlyList<ProjectConfiguration.RequestedPack> requestedPacks
    )
    {
        var catalog = await packCatalog.BrowseAsync(projectDirectory, configuration);
        if (catalog.Value is not { } catalogPacks)
        {
            return ManifestOperationResult<ResolvedPackGraph>.Failure(
                catalog.Error ?? "Unable to browse pack sources."
            );
        }

        var resolvedById = new Dictionary<string, DiscoveredPack>(StringComparer.Ordinal);
        var resolvedPacks = new List<DiscoveredPack>();
        var visiting = new HashSet<PackIdentity>();
        foreach (var rootRequest in requestedPacks)
        {
            var root = PackCatalog.ResolveFromCatalog(
                catalogPacks,
                rootRequest.Id,
                rootRequest.Version
            );
            if (root.Value is not { } rootPack)
            {
                return ManifestOperationResult<ResolvedPackGraph>.Failure(
                    root.Error ?? $"Pack '{rootRequest.Id}' is unavailable."
                );
            }

            var error = ResolveDepthFirst(
                rootPack,
                catalogPacks,
                resolvedById,
                resolvedPacks,
                visiting
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

    private static string? ResolveDepthFirst(
        DiscoveredPack pack,
        IReadOnlyList<CatalogPack> catalog,
        IDictionary<string, DiscoveredPack> resolvedById,
        ICollection<DiscoveredPack> resolvedPacks,
        ISet<PackIdentity> visiting
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
            var dependency = PackCatalog.ResolveFromCatalog(
                catalog,
                reference.Id,
                reference.Version
            );
            if (dependency.Value is not { } dependencyPack)
            {
                return dependency.Error
                    ?? $"Pack '{reference.Id}@{reference.Version}' is unavailable.";
            }

            var error = ResolveDepthFirst(
                dependencyPack,
                catalog,
                resolvedById,
                resolvedPacks,
                visiting
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

    private sealed record PackIdentity(string Id, string Version);
}
