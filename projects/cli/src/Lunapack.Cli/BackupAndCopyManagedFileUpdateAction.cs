namespace Lunapack.Cli;

internal sealed record BackupAndCopyManagedFileUpdateAction(
    PlannedManagedFile ManagedFile,
    ProjectLockFile.ManagedFile? PreviousManagedFile,
    string BackupPath
)
    : PlannedPackUpdateAction(
        ManagedFile.TargetPath,
        ManagedFile.TargetPathRelativeToProject,
        ManagedFile.Contents
    );
