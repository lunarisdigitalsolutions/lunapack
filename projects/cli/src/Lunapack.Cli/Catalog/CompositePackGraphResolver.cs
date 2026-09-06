using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Catalog;

internal sealed class CompositePackGraphResolver(PackCatalog packCatalog, CliConsole console)
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

        var resolvedByKey = new Dictionary<PackResolutionKey, DiscoveredPack>();
        var resolvedPacks = new List<DiscoveredPack>();
        var warnings = new List<string>();
        var acceptedReferences = new HashSet<PackManifest.PackReference>(
            ReferenceEqualityComparer.Instance
        );
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
                    root.Error ?? $"Pack '{rootRequest.Id}' is unavailable.",
                    root.ErrorKind
                );
            }

            var error = ResolveDepthFirst(
                rootPack,
                catalogPacks,
                new Dictionary<string, DiscoveredPack>(StringComparer.Ordinal),
                new HashSet<PackResolutionKey>(),
                resolvedByKey,
                resolvedPacks,
                [],
                warnings,
                acceptedReferences
            );
            if (error is not null)
            {
                return ManifestOperationResult<ResolvedPackGraph>.Failure(error);
            }
        }

        var distinctWarnings = warnings.Distinct(StringComparer.Ordinal).ToList();
        foreach (var warning in distinctWarnings)
        {
            console.Warning(warning);
        }

        return ManifestOperationResult<ResolvedPackGraph>.Success(
            new ResolvedPackGraph(
                resolvedPacks,
                requestedPacks.Select(pack => pack.Id).ToHashSet(StringComparer.Ordinal),
                acceptedReferences
            )
            {
                Warnings = distinctWarnings,
            }
        );
    }

    private static string? ResolveDepthFirst(
        DiscoveredPack pack,
        IReadOnlyList<CatalogPack> catalog,
        IDictionary<string, DiscoveredPack> resolvedById,
        ISet<PackResolutionKey> completed,
        IDictionary<PackResolutionKey, DiscoveredPack> resolvedByKey,
        ICollection<DiscoveredPack> resolvedPacks,
        IList<PackResolutionKey> activePath,
        ICollection<string> warnings,
        ISet<PackManifest.PackReference> acceptedReferences
    )
    {
        var key = PackResolutionKey.Create(pack);
        if (resolvedById.TryGetValue(pack.Manifest.Id, out var resolvedPack))
        {
            var resolvedKey = PackResolutionKey.Create(resolvedPack);
            if (resolvedKey != key)
            {
                return $"Pack '{pack.Manifest.Id}' resolves to conflicting versions '{resolvedPack.Manifest.Version}' and '{pack.Manifest.Version}'.";
            }

            if (completed.Contains(key))
            {
                return null;
            }
        }
        else
        {
            resolvedById.Add(pack.Manifest.Id, pack);
        }

        activePath.Add(key);
        foreach (var reference in pack.Manifest.Packs)
        {
            if (string.Equals(reference.Id, pack.Manifest.Id, StringComparison.Ordinal))
            {
                warnings.Add(
                    $"Ignored self-reference '{Format(key)} -> {reference.Id}@{reference.Version}'."
                );
                continue;
            }

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

            var dependencyKey = PackResolutionKey.Create(dependencyPack);
            var cycleStart = activePath.IndexOf(dependencyKey);
            if (cycleStart >= 0)
            {
                var cycle = activePath.Skip(cycleStart).Append(dependencyKey).Select(Format);
                warnings.Add(
                    $"Ignored dependency cycle '{string.Join(" -> ", cycle)}'; edge '{Format(key)} -> {Format(dependencyKey)}' closes the active path."
                );
                continue;
            }

            var error = ResolveDepthFirst(
                dependencyPack,
                catalog,
                resolvedById,
                completed,
                resolvedByKey,
                resolvedPacks,
                activePath,
                warnings,
                acceptedReferences
            );
            if (error is not null)
            {
                return error;
            }

            acceptedReferences.Add(reference);
        }

        activePath.RemoveAt(activePath.Count - 1);
        completed.Add(key);
        if (resolvedByKey.TryAdd(key, pack))
        {
            resolvedPacks.Add(pack);
        }

        return null;
    }

    private static string Format(PackResolutionKey key) => $"{key.Id}@{key.Version}";

    private sealed record PackResolutionKey(
        string Id,
        string Version,
        ConfiguredSourceIdentity SourceIdentity,
        string? ResolvedCommit
    )
    {
        public static PackResolutionKey Create(DiscoveredPack pack) =>
            new(
                pack.Manifest.Id,
                pack.Manifest.Version,
                pack.SourceIdentity,
                pack.GitSource?.ResolvedCommit
            );
    }
}
