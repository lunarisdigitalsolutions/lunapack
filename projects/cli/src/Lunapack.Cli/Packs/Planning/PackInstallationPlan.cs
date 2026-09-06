using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Planning;

internal sealed record PackInstallationPlan(IReadOnlyList<PlannedManagedFile> ManagedFiles)
{
    public PackInstanceIdentity? RootIdentity { get; init; }

    public IReadOnlyDictionary<string, string> Placements { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlySet<string> IgnoredDeclaredTargets { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<ManagedFileRemapping> Remappings { get; init; } = [];

    public IReadOnlyList<ManagedFileTemplateDiagnostic> Diagnostics { get; init; } = [];
}
