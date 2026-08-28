namespace Lunapack.Cli;

internal sealed record RenderedManagedFileTemplate(
    byte[] Contents,
    IReadOnlyList<ManagedFileTemplateDiagnostic> Diagnostics
);
