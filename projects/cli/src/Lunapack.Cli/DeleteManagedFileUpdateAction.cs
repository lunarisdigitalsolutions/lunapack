namespace Lunapack.Cli;

internal sealed record DeleteManagedFileUpdateAction(
    ProjectLockFile.ResolvedPack PreviousPack,
    ProjectLockFile.ManagedFile PreviousManagedFile,
    string TargetPath
) : PlannedPackUpdateAction(TargetPath, PreviousManagedFile.TargetPath, null);
