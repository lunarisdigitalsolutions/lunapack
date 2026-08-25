namespace Lunapack.Cli;

internal sealed record SkipManagedFileUpdateAction(
    PlannedManagedFile ManagedFile,
    ProjectLockFile.ManagedFile? PreviousManagedFile,
    byte[] ExistingContents
)
    : PlannedPackUpdateAction(
        ManagedFile.TargetPath,
        ManagedFile.TargetPathRelativeToProject,
        ExistingContents
    );
