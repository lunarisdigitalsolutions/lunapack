namespace Lunapack.Cli.Packs;

internal sealed record ResolvedPackParameters(
    IReadOnlyDictionary<string, PackParameterDefinition> Declarations,
    IReadOnlyDictionary<string, ResolvedPackParameterValue> Values
);
