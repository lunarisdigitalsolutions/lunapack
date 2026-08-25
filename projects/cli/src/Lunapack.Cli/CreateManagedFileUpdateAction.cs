namespace Lunapack.Cli;

internal sealed record CreateManagedFileUpdateAction(PlannedManagedFile ManagedFile)
    : PlannedPackUpdateAction(
        ManagedFile.TargetPath,
        ManagedFile.TargetPathRelativeToProject,
        ManagedFile.Contents
    );
