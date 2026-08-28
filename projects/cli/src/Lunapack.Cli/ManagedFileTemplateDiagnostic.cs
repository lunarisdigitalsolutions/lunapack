namespace Lunapack.Cli;

internal sealed record ManagedFileTemplateDiagnostic(
    string ReferencedDeclaredTarget,
    string CurrentEffectiveTarget
);
