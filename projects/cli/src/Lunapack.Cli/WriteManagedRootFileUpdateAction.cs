namespace Lunapack.Cli;

internal sealed record WriteManagedRootFileUpdateAction(
    ManagedRootOwner Owner,
    ManagedRootFile File,
    string TargetPath,
    byte[] Contents
) : PlannedPackUpdateAction(TargetPath, File.TargetPath, Contents);
