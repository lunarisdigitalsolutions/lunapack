namespace Lunapack.Cli;

internal sealed record PackInstallationPlan(IReadOnlyList<PlannedManagedFile> ManagedFiles)
{
    public IReadOnlySet<string> IgnoredDeclaredTargets { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<ManagedFileTemplateDiagnostic> Diagnostics { get; init; } = [];
}
