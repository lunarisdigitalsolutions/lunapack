namespace Lunapack.Cli.Catalog;

internal static class CatalogPackFilter
{
    public static IReadOnlyList<CatalogPack> DirectInstallable(
        IReadOnlyList<CatalogPack> catalog
    ) => [.. catalog.Where(pack => pack.Manifest.DirectInstallable)];

    public static IReadOnlyList<CatalogPack> ForBrowsing(
        IReadOnlyList<CatalogPack> catalog,
        bool onlyComposites
    ) =>
        [
            .. catalog.Where(pack =>
                pack.Manifest.DirectInstallable
                && (!onlyComposites || pack.Manifest.Packs.Count > 0)
            ),
        ];
}
