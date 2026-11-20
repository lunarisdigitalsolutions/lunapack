using System.IO.Abstractions;
using System.Security.Cryptography;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.ManagedFiles;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class PackUpdateTransaction(IFileSystem fileSystem, CliConsole console)
{
    public ManifestOperationResult<Rollback> Apply(
        string projectDirectory,
        PackUpdatePlan updatePlan
    )
    {
        var rollback = new Rollback(fileSystem, console, projectDirectory);
        try
        {
            foreach (var action in updatePlan.Actions)
            {
                ValidateActionPath(projectDirectory, action);
            }

            foreach (var action in updatePlan.Actions)
            {
                ApplyAction(action, rollback);
            }

            return ManifestOperationResult<Rollback>.Success(rollback);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
            )
        {
            rollback.Restore();
            return ManifestOperationResult<Rollback>.Failure(
                $"Unable to apply pack update: {exception.Message}"
            );
        }
    }

    private void ValidateActionPath(string projectDirectory, PlannedPackUpdateAction action)
    {
        ProjectMutationPathSecurity.EnsureNoAliases(
            fileSystem,
            projectDirectory,
            action.TargetPath
        );
        if (action is BackupAndCopyManagedFileUpdateAction backupAction)
        {
            ProjectMutationPathSecurity.EnsureNoAliases(
                fileSystem,
                projectDirectory,
                backupAction.BackupPath
            );
        }
    }

    private void ApplyAction(PlannedPackUpdateAction action, Rollback rollback)
    {
        switch (action)
        {
            case SkipManagedFileUpdateAction:
                console.Verbose($"Skipped managed file '{action.TargetPathRelativeToProject}'.");
                return;
            case DeleteManagedFileUpdateAction deleteAction:
                DeleteTarget(deleteAction, rollback);
                return;
            case PreserveManagedFileUpdateAction:
                return;
            case BackupAndCopyManagedFileUpdateAction backupAction:
                BackupAndWrite(backupAction, rollback);
                return;
            default:
                WriteTarget(action, rollback);
                return;
        }
    }

    private void BackupAndWrite(BackupAndCopyManagedFileUpdateAction action, Rollback rollback)
    {
        rollback.Snapshot(action.TargetPath);
        CreateTargetDirectory(action.BackupPath, rollback);
        fileSystem.File.Move(action.TargetPath, action.BackupPath);
        rollback.TrackCreatedFile(action.BackupPath);
        console.Debug(
            $"Backed up managed file '{action.TargetPathRelativeToProject}' to '{action.BackupPath}'."
        );
        WriteTarget(action, rollback);
    }

    private void DeleteTarget(DeleteManagedFileUpdateAction action, Rollback rollback)
    {
        ProjectMutationPathSecurity.EnsureNoAliases(
            fileSystem,
            rollback.ProjectDirectory,
            action.TargetPath
        );
        if (!fileSystem.File.Exists(action.TargetPath))
        {
            throw new InvalidOperationException(
                $"Managed target '{action.TargetPathRelativeToProject}' changed after the update was planned."
            );
        }

        var plannedContents = fileSystem.File.ReadAllBytes(action.TargetPath);
        var currentSha256 = Convert.ToHexString(SHA256.HashData(plannedContents));
        if (
            !string.Equals(
                currentSha256,
                action.PlannedTargetSha256,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidOperationException(
                $"Managed target '{action.TargetPathRelativeToProject}' changed after the update was planned."
            );
        }

        ProjectMutationPathSecurity.EnsureNoAliases(
            fileSystem,
            rollback.ProjectDirectory,
            action.TargetPath
        );
        var confirmedContents = fileSystem.File.ReadAllBytes(action.TargetPath);
        var confirmedSha256 = Convert.ToHexString(SHA256.HashData(confirmedContents));
        if (
            !string.Equals(
                confirmedSha256,
                action.PlannedTargetSha256,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidOperationException(
                $"Managed target '{action.TargetPathRelativeToProject}' changed after the update was planned."
            );
        }

        rollback.Snapshot(action.TargetPath, confirmedContents);
        if (fileSystem.File.Exists(action.TargetPath))
        {
            fileSystem.File.Delete(action.TargetPath);
            console.Debug($"Deleted managed file '{action.TargetPath}'.");
        }
    }

    private void WriteTarget(PlannedPackUpdateAction action, Rollback rollback)
    {
        var contents =
            action.ResultingContents
            ?? throw new InvalidOperationException(
                $"Update action for '{action.TargetPathRelativeToProject}' has no resulting content."
            );
        rollback.Snapshot(action.TargetPath);
        CreateTargetDirectory(action.TargetPath, rollback);
        ProjectMutationPathSecurity.ReplaceFile(
            fileSystem,
            rollback.ProjectDirectory,
            action.TargetPath,
            contents
        );
        console.Debug($"Wrote managed file '{action.TargetPathRelativeToProject}'.");
    }

    private void CreateTargetDirectory(string targetPath, Rollback rollback)
    {
        var targetDirectory = fileSystem.Path.GetDirectoryName(targetPath);
        var missingDirectories = new Stack<string>();
        while (targetDirectory is not null && !fileSystem.Directory.Exists(targetDirectory))
        {
            missingDirectories.Push(targetDirectory);
            targetDirectory = fileSystem.Path.GetDirectoryName(targetDirectory);
        }

        foreach (var directory in missingDirectories)
        {
            fileSystem.Directory.CreateDirectory(directory);
            rollback.TrackCreatedDirectory(directory);
            console.Verbose($"Created directory '{directory}'.");
        }
    }

    internal sealed class Rollback(
        IFileSystem fileSystem,
        CliConsole console,
        string projectDirectory
    )
    {
        private readonly List<string> _createdDirectories = [];
        private readonly List<string> _createdFiles = [];
        private readonly HashSet<string> _createdFilePaths = new(
            ProjectPath.ProjectRelativePathComparer(fileSystem)
        );
        private readonly Dictionary<string, byte[]> _snapshots = new(
            ProjectPath.ProjectRelativePathComparer(fileSystem)
        );

        public string ProjectDirectory { get; } = projectDirectory;

        public void Restore()
        {
            foreach (var createdFile in _createdFiles.AsEnumerable().Reverse())
            {
                if (fileSystem.File.Exists(createdFile))
                {
                    ProjectMutationPathSecurity.EnsureNoAliases(
                        fileSystem,
                        ProjectDirectory,
                        createdFile
                    );
                    fileSystem.File.Delete(createdFile);
                    console.Verbose($"Removed file created during rollback '{createdFile}'.");
                }
            }

            foreach (var (path, contents) in _snapshots)
            {
                ProjectMutationPathSecurity.ReplaceFile(
                    fileSystem,
                    ProjectDirectory,
                    path,
                    contents
                );
                console.Verbose($"Restored managed file '{path}' from rollback snapshot.");
            }

            foreach (var createdDirectory in _createdDirectories.AsEnumerable().Reverse())
            {
                var createdDirectoryIsEmpty =
                    fileSystem.Directory.Exists(createdDirectory)
                    && !fileSystem.Directory.EnumerateFileSystemEntries(createdDirectory).Any();
                if (createdDirectoryIsEmpty)
                {
                    ProjectMutationPathSecurity.EnsureNoAliases(
                        fileSystem,
                        ProjectDirectory,
                        createdDirectory
                    );
                    fileSystem.Directory.Delete(createdDirectory);
                    console.Verbose(
                        $"Removed directory created during rollback '{createdDirectory}'."
                    );
                }
            }
        }

        public void Snapshot(string path)
        {
            if (_snapshots.ContainsKey(path) || _createdFilePaths.Contains(path))
            {
                return;
            }

            if (fileSystem.File.Exists(path))
            {
                _snapshots.Add(path, fileSystem.File.ReadAllBytes(path));
            }
            else
            {
                TrackCreatedFile(path);
            }
        }

        public void Snapshot(string path, byte[] contents)
        {
            if (!_createdFilePaths.Contains(path))
            {
                _snapshots.TryAdd(path, contents);
            }
        }

        public void TrackCreatedDirectory(string path) => _createdDirectories.Add(path);

        public void TrackCreatedFile(string path)
        {
            if (_createdFilePaths.Add(path))
            {
                _createdFiles.Add(path);
            }
        }
    }
}
