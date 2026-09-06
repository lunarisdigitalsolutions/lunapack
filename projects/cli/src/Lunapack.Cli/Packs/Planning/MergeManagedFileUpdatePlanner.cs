using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Planning;

internal static class MergeManagedFileUpdatePlanner
{
    public static ManifestOperationResult<PlannedPackUpdateAction> Plan(
        PlannedManagedFile managedFile,
        ProjectLockFile.ManagedFile? previousManagedFile,
        byte[] targetContents
    )
    {
        var mergedContents = managedFile.Strategy.Method switch
        {
            "lines" => LinesManagedFileMerger.Merge(targetContents, managedFile.Contents),
            "section" => SectionManagedFileMerger.Merge(targetContents, managedFile.Contents),
            "json" => JsonManagedFileMerger.Merge(targetContents, managedFile.Contents),
            _ => ManifestOperationResult<byte[]>.Failure(
                $"Managed target '{managedFile.TargetPathRelativeToProject}' uses unsupported merge method '{managedFile.Strategy.Method}'."
            ),
        };
        if (mergedContents.Value is not { } contents)
        {
            return ManifestOperationResult<PlannedPackUpdateAction>.Failure(
                mergedContents.Error ?? "Unable to merge managed file."
            );
        }

        return ManifestOperationResult<PlannedPackUpdateAction>.Success(
            managedFile.Strategy.Method switch
            {
                "lines" => new MergeLinesManagedFileUpdateAction(
                    managedFile,
                    previousManagedFile,
                    contents
                ),
                "section" => new MergeSectionManagedFileUpdateAction(
                    managedFile,
                    previousManagedFile,
                    contents
                ),
                "json" => new MergeJsonManagedFileUpdateAction(
                    managedFile,
                    previousManagedFile,
                    contents
                ),
                _ => throw new InvalidOperationException("Unsupported merge method."),
            }
        );
    }
}
