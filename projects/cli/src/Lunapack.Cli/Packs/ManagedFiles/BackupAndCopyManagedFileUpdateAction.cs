using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.ManagedFiles;

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
