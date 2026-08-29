namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record ManagedFileTemplateDiagnostic(
    string ReferencedDeclaredTarget,
    string CurrentEffectiveTarget
);
