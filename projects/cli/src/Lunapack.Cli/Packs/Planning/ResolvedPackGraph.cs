using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
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
