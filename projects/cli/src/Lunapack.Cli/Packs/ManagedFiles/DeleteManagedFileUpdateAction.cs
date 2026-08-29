namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record DeleteManagedFileUpdateAction(
    ManagedRootOwner PreviousOwner,
    ManagedRootFile PreviousFile,
    string TargetPath
) : PlannedPackUpdateAction(TargetPath, PreviousFile.TargetPath, null);
