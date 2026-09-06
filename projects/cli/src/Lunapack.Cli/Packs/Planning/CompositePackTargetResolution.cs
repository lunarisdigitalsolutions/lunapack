namespace Lunapack.Cli.Packs.Planning;

internal sealed record CompositePackTargetResolution(
    string EffectiveTarget,
    string ParentPackId,
    int ReferenceDistance
);
