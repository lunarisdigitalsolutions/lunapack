namespace Lunapack.Cli.Packs.ManagedFiles;

internal static class ManagedFileRemappingFormatter
{
    public static string Format(ManagedFileRemapping remapping) =>
        $"remap: {remapping.PackId} {remapping.DeclaredTarget} -> {remapping.EffectiveTarget} source: {FormatOrigin(remapping)}";

    private static string FormatOrigin(ManagedFileRemapping remapping) =>
        remapping.Origin switch
        {
            ManagedFileRemappingOrigin.Command => "command line",
            ManagedFileRemappingOrigin.Pack => $"pack '{remapping.PackId}' in lunapack.yml",
            ManagedFileRemappingOrigin.Project => "top-level remap in lunapack.yml",
            ManagedFileRemappingOrigin.Lock => "lunapack-lock.yml",
            _ => throw new ArgumentOutOfRangeException(
                nameof(remapping),
                remapping.Origin,
                "Unsupported managed-file remapping origin."
            ),
        };
}
