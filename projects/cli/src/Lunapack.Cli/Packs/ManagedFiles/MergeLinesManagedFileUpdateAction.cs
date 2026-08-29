using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record MergeLinesManagedFileUpdateAction(
    PlannedManagedFile ManagedFile,
    ProjectLockFile.ManagedFile? PreviousManagedFile,
    byte[] MergedContents
)
    : PlannedPackUpdateAction(
        ManagedFile.TargetPath,
        ManagedFile.TargetPathRelativeToProject,
        MergedContents
    );
