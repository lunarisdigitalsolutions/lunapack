namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record CreateManagedFileUpdateAction(PlannedManagedFile ManagedFile)
    : PlannedPackUpdateAction(
        ManagedFile.TargetPath,
        ManagedFile.TargetPathRelativeToProject,
        ManagedFile.Contents
    );
