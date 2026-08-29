namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record RenderedManagedFileTemplate(
    byte[] Contents,
    IReadOnlyList<ManagedFileTemplateDiagnostic> Diagnostics
);
