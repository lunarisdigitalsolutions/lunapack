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
    public IReadOnlyList<string> Warnings { get; init; } = [];

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
        var packsById = Packs.ToLookup(pack => pack.Manifest.Id, StringComparer.Ordinal);
        var selectedPacks = new HashSet<DiscoveredPack>();
        var activeReferences = new HashSet<PackManifest.PackReference>(
            ReferenceEqualityComparer.Instance
        );
        foreach (var root in Packs.Where(IsRoot))
        {
            var selectionError = Select(
                root,
                parameters,
                packsById,
                selectedPacks,
                activeReferences,
                ActiveReferences
            );
            if (selectionError is not null)
            {
                return ManifestOperationResult<ResolvedPackGraph>.Failure(selectionError);
            }
        }

        return ManifestOperationResult<ResolvedPackGraph>.Success(
            new ResolvedPackGraph(
                [.. Packs.Where(selectedPacks.Contains)],
                RootPackIds,
                activeReferences
            )
            {
                Warnings = Warnings,
            }
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
        ILookup<string, DiscoveredPack> packsById,
        ISet<DiscoveredPack> selectedPacks,
        ISet<PackManifest.PackReference> activeReferences,
        IReadOnlySet<PackManifest.PackReference>? allowedReferences
    )
    {
        if (!selectedPacks.Add(pack))
        {
            return null;
        }

        foreach (var reference in pack.Manifest.Packs)
        {
            if (allowedReferences is not null && !allowedReferences.Contains(reference))
            {
                continue;
            }

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
                packsById[reference.Id]
                    .FirstOrDefault(candidate =>
                        string.Equals(
                            candidate.Manifest.Version,
                            reference.Version,
                            StringComparison.Ordinal
                        )
                    )
                    is { } dependency
                && Select(
                    dependency,
                    parameters,
                    packsById,
                    selectedPacks,
                    activeReferences,
                    allowedReferences
                )
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
