using Lunapack.Cli.Packs.ManagedFiles;

namespace Lunapack.Cli.Packs.Planning;

internal sealed record PackInstallationPlan(IReadOnlyList<PlannedManagedFile> ManagedFiles)
{
    public IReadOnlySet<string> IgnoredDeclaredTargets { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<ManagedFileRemapping> Remappings { get; init; } = [];

    public IReadOnlyList<ManagedFileTemplateDiagnostic> Diagnostics { get; init; } = [];
}
