namespace Lunapack.Cli;

internal sealed record DeleteManagedFileUpdateAction(
    ManagedRootOwner PreviousOwner,
    ManagedRootFile PreviousFile,
    string TargetPath
) : PlannedPackUpdateAction(TargetPath, PreviousFile.TargetPath, null);
