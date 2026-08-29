namespace Lunapack.Cli.Catalog;

internal static class CatalogOutputFormatter
{
    private const int DescriptionPreviewLength = 80;

    public static string Format(CatalogPack pack)
    {
        var packageReference = $"{pack.Manifest.Id}@{pack.Manifest.Version}";
        return string.IsNullOrEmpty(pack.Manifest.Description)
            ? packageReference
            : $"{packageReference} - {Preview(pack.Manifest.Description)}";
    }

    public static string FormatSearchPackage(string packageId) => packageId;

    public static string FormatSearchVersion(CatalogPack pack)
    {
        var version = $"  {pack.Manifest.Version}";
        return string.IsNullOrEmpty(pack.Manifest.Description)
            ? version
            : $"{version} - {Preview(pack.Manifest.Description)}";
    }

    private static string Preview(string description) =>
        description.Length <= DescriptionPreviewLength
            ? description
            : string.Concat(description.AsSpan(0, DescriptionPreviewLength - 3), "...");
}
