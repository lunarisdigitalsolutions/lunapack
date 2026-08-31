namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record ManagedFileRemapping(
    string PackId,
    string DeclaredTarget,
    string EffectiveTarget,
    ManagedFileRemappingOrigin Origin
);
