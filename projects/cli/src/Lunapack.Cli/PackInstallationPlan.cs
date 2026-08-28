namespace Lunapack.Cli;

internal sealed record PackInstallationPlan(IReadOnlyList<PlannedManagedFile> ManagedFiles)
{
    public IReadOnlyList<ManagedFileTemplateDiagnostic> Diagnostics { get; init; } = [];
}
