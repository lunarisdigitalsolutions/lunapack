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

        var packsById = Packs.ToDictionary(
            candidate => candidate.Manifest.Id,
            StringComparer.Ordinal
        );
        var candidates = new List<CompositePackTargetResolution>();
        foreach (var root in Packs.Where(IsRoot))
        {
            FindCompositeTargets(root, pack, declaredTarget, packsById, [], candidates);
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

    private void FindCompositeTargets(
        DiscoveredPack current,
        DiscoveredPack targetPack,
        string declaredTarget,
        IReadOnlyDictionary<string, DiscoveredPack> packsById,
        IReadOnlyList<CompositeReferenceLayer> path,
        ICollection<CompositePackTargetResolution> candidates
    )
    {
        if (string.Equals(current.Manifest.Id, targetPack.Manifest.Id, StringComparison.Ordinal))
        {
            for (var index = path.Count - 1; index >= 0; index--)
            {
                var layer = path[index];
                var effectiveTarget = ManagedFileTargetRemapping
                    .FromManifest(layer.Reference.Remap)
                    .TryResolve(declaredTarget);
                if (effectiveTarget is not null)
                {
                    candidates.Add(
                        new CompositePackTargetResolution(
                            effectiveTarget,
                            layer.ParentPackId,
                            path.Count - index
                        )
                    );
                    break;
                }
            }

            return;
        }

        foreach (var reference in current.Manifest.Packs.Where(IsActive))
        {
            if (!packsById.TryGetValue(reference.Id, out var dependency))
            {
                continue;
            }

            FindCompositeTargets(
                dependency,
                targetPack,
                declaredTarget,
                packsById,
                [.. path, new CompositeReferenceLayer(current.Manifest.Id, reference)],
                candidates
            );
        }
    }

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

    private sealed record CompositeReferenceLayer(
        string ParentPackId,
        PackManifest.PackReference Reference
    );
}
