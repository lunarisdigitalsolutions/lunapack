namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record WriteManagedRootFileUpdateAction(
    ManagedRootOwner Owner,
    ManagedRootFile File,
    string TargetPath,
    byte[] Contents
) : PlannedPackUpdateAction(TargetPath, File.TargetPath, Contents);
