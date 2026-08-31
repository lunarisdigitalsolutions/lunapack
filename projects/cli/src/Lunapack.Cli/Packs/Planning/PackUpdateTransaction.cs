using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.ManagedFiles;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class PackUpdateTransaction(IFileSystem fileSystem, CliConsole console)
{
    public ManifestOperationResult<Rollback> Apply(PackUpdatePlan updatePlan)
    {
        var rollback = new Rollback(fileSystem, console);
        try
        {
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

    private void ApplyAction(PlannedPackUpdateAction action, Rollback rollback)
    {
        switch (action)
        {
            case SkipManagedFileUpdateAction:
                console.Verbose($"Skipped managed file '{action.TargetPathRelativeToProject}'.");
                return;
            case DeleteManagedFileUpdateAction:
                DeleteTarget(action.TargetPath, rollback);
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

    private void DeleteTarget(string targetPath, Rollback rollback)
    {
        rollback.Snapshot(targetPath);
        if (fileSystem.File.Exists(targetPath))
        {
            fileSystem.File.Delete(targetPath);
            console.Debug($"Deleted managed file '{targetPath}'.");
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
        fileSystem.File.WriteAllBytes(action.TargetPath, contents);
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

    internal sealed class Rollback(IFileSystem fileSystem, CliConsole console)
    {
        private readonly List<string> _createdDirectories = [];
        private readonly List<string> _createdFiles = [];
        private readonly Dictionary<string, byte[]> _snapshots = new(StringComparer.Ordinal);

        public void Restore()
        {
            foreach (var createdFile in _createdFiles.AsEnumerable().Reverse())
            {
                if (fileSystem.File.Exists(createdFile))
                {
                    fileSystem.File.Delete(createdFile);
                    console.Verbose($"Removed file created during rollback '{createdFile}'.");
                }
            }

            foreach (var (path, contents) in _snapshots)
            {
                fileSystem.File.WriteAllBytes(path, contents);
                console.Verbose($"Restored managed file '{path}' from rollback snapshot.");
            }

            foreach (var createdDirectory in _createdDirectories.AsEnumerable().Reverse())
            {
                var createdDirectoryIsEmpty =
                    fileSystem.Directory.Exists(createdDirectory)
                    && !fileSystem.Directory.EnumerateFileSystemEntries(createdDirectory).Any();
                if (createdDirectoryIsEmpty)
                {
                    fileSystem.Directory.Delete(createdDirectory);
                    console.Verbose(
                        $"Removed directory created during rollback '{createdDirectory}'."
                    );
                }
            }
        }

        public void Snapshot(string path)
        {
            if (_snapshots.ContainsKey(path))
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

        public void TrackCreatedDirectory(string path) => _createdDirectories.Add(path);

        public void TrackCreatedFile(string path) => _createdFiles.Add(path);
    }
}
