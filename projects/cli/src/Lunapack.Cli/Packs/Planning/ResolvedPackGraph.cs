using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.Packs.Planning;

internal sealed record ResolvedPackGraph(
    IReadOnlyList<DiscoveredPack> Packs,
    IReadOnlySet<string>? RootPackIds = null,
    IReadOnlySet<PackManifest.PackReference>? ActiveReferences = null
)
{
    public IReadOnlyList<PackManifest.PackReference> GetIncomingReferences(DiscoveredPack pack) =>
        [
            .. Packs
                .SelectMany(parent => parent.Manifest.Packs)
                .Where(reference =>
                    (ActiveReferences is null || ActiveReferences.Contains(reference))
                    && string.Equals(reference.Id, pack.Manifest.Id, StringComparison.Ordinal)
                    && string.Equals(
                        reference.Version,
                        pack.Manifest.Version,
                        StringComparison.Ordinal
                    )
                ),
        ];

    public ManifestOperationResult<CompositePackTargetResolution?> ResolveCompositeTarget(
        DiscoveredPack pack,
        string declaredTarget
    )
    {
        if (IsRoot(pack))
        {
            return ManifestOperationResult<CompositePackTargetResolution?>.Success(null);
        }

        var distances = CalculateReferenceDistancesTo(pack.Manifest.Id);
        var candidates = new List<CompositePackTargetResolution>();
        foreach (var parent in Packs)
        {
            foreach (var reference in parent.Manifest.Packs.Where(IsActive))
            {
                if (!distances.TryGetValue(reference.Id, out var dependencyDistance))
                {
                    continue;
                }

                var effectiveTarget = ManagedFileTargetRemapping
                    .FromManifest(reference.Remap)
                    .TryResolve(declaredTarget);
                if (effectiveTarget is not null)
                {
                    candidates.Add(
                        new CompositePackTargetResolution(
                            effectiveTarget,
                            parent.Manifest.Id,
                            dependencyDistance + 1
                        )
                    );
                }
            }
        }

        if (candidates.Count == 0)
        {
            return ManifestOperationResult<CompositePackTargetResolution?>.Success(null);
        }

        var nearestDistance = candidates.Min(candidate => candidate.ReferenceDistance);
        var nearest = candidates
            .Where(candidate => candidate.ReferenceDistance == nearestDistance)
            .ToArray();
        var effectiveTargets = nearest
            .Select(candidate => candidate.EffectiveTarget)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return effectiveTargets.Length == 1
            ? ManifestOperationResult<CompositePackTargetResolution?>.Success(nearest[0])
            : ManifestOperationResult<CompositePackTargetResolution?>.Failure(
                $"Pack '{pack.Manifest.Id}' target '{declaredTarget}' has conflicting composite remaps at the same reference depth."
            );
    }

    private Dictionary<string, int> CalculateReferenceDistancesTo(string targetPackId)
    {
        var parentsByPackId = Packs.ToDictionary(
            pack => pack.Manifest.Id,
            _ => new List<string>(),
            StringComparer.Ordinal
        );
        foreach (var parent in Packs)
        {
            foreach (var reference in parent.Manifest.Packs.Where(IsActive))
            {
                if (parentsByPackId.TryGetValue(reference.Id, out var parents))
                {
                    parents.Add(parent.Manifest.Id);
                }
            }
        }

        if (!parentsByPackId.ContainsKey(targetPackId))
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [targetPackId] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(targetPackId);
        while (queue.TryDequeue(out var packId))
        {
            foreach (var parentId in parentsByPackId[packId])
            {
                if (distances.ContainsKey(parentId))
                {
                    continue;
                }

                distances[parentId] = distances[packId] + 1;
                queue.Enqueue(parentId);
            }
        }

        return distances;
    }

    public ManifestOperationResult<ResolvedPackGraph> Select(ResolvedPackParameters parameters)
    {
        var packsById = Packs.ToDictionary(pack => pack.Manifest.Id, StringComparer.Ordinal);
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);
        var activeReferences = new HashSet<PackManifest.PackReference>(
            ReferenceEqualityComparer.Instance
        );
        foreach (var root in Packs.Where(IsRoot))
        {
            var selectionError = Select(root, parameters, packsById, selectedIds, activeReferences);
            if (selectionError is not null)
            {
                return ManifestOperationResult<ResolvedPackGraph>.Failure(selectionError);
            }
        }

        return ManifestOperationResult<ResolvedPackGraph>.Success(
            new ResolvedPackGraph(
                [.. Packs.Where(pack => selectedIds.Contains(pack.Manifest.Id))],
                RootPackIds,
                activeReferences
            )
        );
    }

    public bool IsRoot(DiscoveredPack pack) =>
        RootPackIds?.Contains(pack.Manifest.Id) is true
        || (
            RootPackIds is null
            && string.Equals(pack.Manifest.Id, Packs[^1].Manifest.Id, StringComparison.Ordinal)
        );

    private bool IsActive(PackManifest.PackReference reference) =>
        ActiveReferences is null || ActiveReferences.Contains(reference);

    private static string? Select(
        DiscoveredPack pack,
        ResolvedPackParameters parameters,
        IReadOnlyDictionary<string, DiscoveredPack> packsById,
        ISet<string> selectedIds,
        ISet<PackManifest.PackReference> activeReferences
    )
    {
        if (!selectedIds.Add(pack.Manifest.Id))
        {
            return null;
        }

        foreach (var reference in pack.Manifest.Packs)
        {
            var selected = IsSelected(reference, parameters);
            if (!selected.IsSuccess)
            {
                return selected.Error
                    ?? $"Unable to evaluate pack reference condition for '{reference.Id}'.";
            }

            if (!selected.Value)
            {
                continue;
            }

            activeReferences.Add(reference);
            if (
                packsById.TryGetValue(reference.Id, out var dependency)
                && Select(dependency, parameters, packsById, selectedIds, activeReferences)
                    is { } error
            )
            {
                return error;
            }
        }

        return null;
    }

    private static ManifestOperationResult<bool> IsSelected(
        PackManifest.PackReference reference,
        ResolvedPackParameters parameters
    )
    {
        if (reference.Condition is null)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        var condition = ManagedFileConditionParser.Parse(
            reference.Condition,
            parameters.Declarations
        );
        return condition.Value is { } parsedCondition
            ? ManifestOperationResult<bool>.Success(parsedCondition.Evaluate(parameters.Values))
            : ManifestOperationResult<bool>.Failure(
                condition.Error ?? "Unable to evaluate pack reference condition."
            );
    }
}
