namespace Lunapack.Cli;

internal sealed record PackInstallationPlan(IReadOnlyList<PlannedManagedFile> ManagedFiles);
