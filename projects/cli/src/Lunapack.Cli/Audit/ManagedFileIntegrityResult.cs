using Lunapack.Cli.Packs.ManagedFiles;

namespace Lunapack.Cli.Audit;

internal sealed record ManagedFileIntegrityResult(
    ManagedRootOwner Owner,
    string TargetPath,
    ManagedFileIntegrityStatus Status
)
{
    public bool IsViolation => Status is not ManagedFileIntegrityStatus.Current;
}
