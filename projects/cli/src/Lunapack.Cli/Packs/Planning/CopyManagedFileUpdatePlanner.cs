using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class CopyManagedFileUpdatePlanner(IFileSystem fileSystem)
{
    public ManifestOperationResult<PlannedPackUpdateAction> Plan(
        PlannedManagedFile managedFile,
        ProjectLockFile.ManagedFile? previousManagedFile,
        byte[] targetContents
    ) =>
        managedFile.Strategy.Method switch
        {
            "overwrite" => ManifestOperationResult<PlannedPackUpdateAction>.Success(
                new CopyManagedFileUpdateAction(managedFile, previousManagedFile)
            ),
            "fail-if-exists" => ManifestOperationResult<PlannedPackUpdateAction>.Failure(
                $"Managed target '{managedFile.TargetPathRelativeToProject}' already exists."
            ),
            "skip-if-exists" => ManifestOperationResult<PlannedPackUpdateAction>.Success(
                new SkipManagedFileUpdateAction(managedFile, previousManagedFile, targetContents)
            ),
            "backup-and-overwrite" => ManifestOperationResult<PlannedPackUpdateAction>.Success(
                new BackupAndCopyManagedFileUpdateAction(
                    managedFile,
                    previousManagedFile,
                    CreateBackupPath(managedFile.TargetPath)
                )
            ),
            _ => ManifestOperationResult<PlannedPackUpdateAction>.Failure(
                $"Managed target '{managedFile.TargetPathRelativeToProject}' uses unsupported copy method '{managedFile.Strategy.Method}'."
            ),
        };

    private string CreateBackupPath(string targetPath)
    {
        var suffix = 1;
        var backupPath = $"{targetPath}.{suffix}";
        while (fileSystem.File.Exists(backupPath))
        {
            suffix++;
            backupPath = $"{targetPath}.{suffix}";
        }

        return backupPath;
    }
}
