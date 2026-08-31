namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record ManagedFileTargetResolution(
    string EffectiveTarget,
    ManagedFileRemapping? Remapping = null
);
