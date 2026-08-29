using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Packs.Planning;

internal static class LockedSourceUpdateSelector
{
    public static CatalogPack? SelectOrdinary(
        ProjectLockFile.ResolvedPack currentPack,
        IReadOnlyList<CatalogPack> catalog
    ) => SelectFromSource(currentPack, catalog, requestedVersion: null).Value;

    public static ManifestOperationResult<ExplicitSelection> SelectExplicit(
        ProjectLockFile.ResolvedPack currentPack,
        IReadOnlyList<CatalogPack> catalog,
        string requestedVersion
    )
    {
        var lockedSource = SelectFromSource(currentPack, catalog, requestedVersion);
        if (lockedSource.Value is { } lockedCandidate)
        {
            return ManifestOperationResult<ExplicitSelection>.Success(
                new ExplicitSelection(lockedCandidate, null)
            );
        }

        var selected = PackCatalog.SelectFromCatalog(catalog, currentPack.Id, requestedVersion);
        if (selected.Value is not { } candidate)
        {
            return ManifestOperationResult<ExplicitSelection>.Failure(
                selected.Error ?? $"Pack '{currentPack.Id}' is unavailable."
            );
        }

        return ManifestOperationResult<ExplicitSelection>.Success(
            new ExplicitSelection(candidate, CreateSourceSwitch(currentPack, candidate))
        );
    }

    private static ManifestOperationResult<CatalogPack> SelectFromSource(
        ProjectLockFile.ResolvedPack currentPack,
        IReadOnlyList<CatalogPack> catalog,
        string? requestedVersion
    ) =>
        PackCatalog.SelectFromCatalog(
            currentPack.SourceIdentity is { } sourceIdentity
                ?
                [
                    .. catalog.Where(candidate =>
                        string.Equals(
                            candidate.Manifest.Id,
                            currentPack.Id,
                            StringComparison.Ordinal
                        )
                        && candidate.SourceIdentity == sourceIdentity
                    ),
                ]
                : catalog,
            currentPack.Id,
            requestedVersion
        );

    private static SourceSwitch? CreateSourceSwitch(
        ProjectLockFile.ResolvedPack currentPack,
        CatalogPack candidate
    )
    {
        if (
            currentPack.SourceIdentity is not { } sourceIdentity
            || candidate.SourceIdentity == sourceIdentity
        )
        {
            return null;
        }

        return new SourceSwitch(currentPack.Id, sourceIdentity, candidate.SourceIdentity);
    }

    internal sealed record ExplicitSelection(CatalogPack Candidate, SourceSwitch? SourceSwitch);

    internal sealed record SourceSwitch(
        string PackId,
        ConfiguredSourceIdentity CurrentSource,
        ConfiguredSourceIdentity SelectedSource
    );
}
