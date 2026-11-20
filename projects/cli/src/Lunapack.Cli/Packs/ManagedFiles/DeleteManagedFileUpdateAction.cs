namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record DeleteManagedFileUpdateAction(
    ManagedRootOwner PreviousOwner,
    ManagedRootFile PreviousFile,
    string TargetPath,
    string PlannedTargetSha256,
    bool WasModifiedAtPlanning = false
) : PlannedPackUpdateAction(TargetPath, PreviousFile.TargetPath, null);
