using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record CopyManagedFileUpdateAction(
    PlannedManagedFile ManagedFile,
    ProjectLockFile.ManagedFile? PreviousManagedFile
)
    : PlannedPackUpdateAction(
        ManagedFile.TargetPath,
        ManagedFile.TargetPathRelativeToProject,
        ManagedFile.Contents
    );
