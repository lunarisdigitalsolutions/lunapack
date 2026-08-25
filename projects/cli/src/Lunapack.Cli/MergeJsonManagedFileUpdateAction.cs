namespace Lunapack.Cli;

internal sealed record MergeJsonManagedFileUpdateAction(
    PlannedManagedFile ManagedFile,
    ProjectLockFile.ManagedFile? PreviousManagedFile,
    byte[] MergedContents
)
    : PlannedPackUpdateAction(
        ManagedFile.TargetPath,
        ManagedFile.TargetPathRelativeToProject,
        MergedContents
    );
