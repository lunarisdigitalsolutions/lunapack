using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;

namespace Lunapack.Cli.Packs;

internal sealed class PackSnapshotBudget(string packId, ResourceLimits resourceLimits)
{
    private readonly HashSet<string> _directoryPaths = new(StringComparer.Ordinal);

    public long BytesObserved { get; private set; }

    public long DirectoriesObserved { get; private set; }

    public long EntriesObserved { get; private set; }

    public long FilesObserved { get; private set; }

    public ManifestOperationResult<bool> AddDirectory()
    {
        var observedEntries = checked(EntriesObserved + 1);
        if (observedEntries > resourceLimits.FilesPerPack)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Pack '{packId}' snapshot exceeds its {resourceLimits.FilesPerPack} file and directory limit (at least {observedEntries} entries observed).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        EntriesObserved = observedEntries;
        DirectoriesObserved = checked(DirectoriesObserved + 1);
        return ManifestOperationResult<bool>.Success(true);
    }

    public ManifestOperationResult<bool> AddFile(long observedBytes = 0)
    {
        var observedEntries = checked(EntriesObserved + 1);
        if (observedEntries > resourceLimits.FilesPerPack)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Pack '{packId}' snapshot exceeds its {resourceLimits.FilesPerPack} file and directory limit (at least {observedEntries} entries observed).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        EntriesObserved = observedEntries;
        FilesObserved = checked(FilesObserved + 1);
        return AddBytes(observedBytes);
    }

    public ManifestOperationResult<bool> AddFilePath(string relativePath, long observedBytes)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        var separator = normalizedPath.IndexOf('/');
        while (separator >= 0)
        {
            var parent = normalizedPath[..separator];
            if (!string.IsNullOrEmpty(parent) && _directoryPaths.Add(parent))
            {
                var directory = AddDirectory();
                if (!directory.IsSuccess)
                {
                    return directory;
                }
            }

            separator = normalizedPath.IndexOf('/', separator + 1);
        }

        return AddFile(observedBytes);
    }

    public ManifestOperationResult<bool> AddBytes(long observedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedBytes);

        var totalBytes = checked(BytesObserved + observedBytes);
        if (totalBytes > resourceLimits.PackSnapshotBytes)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Pack '{packId}' snapshot exceeds its {resourceLimits.PackSnapshotBytes} byte limit (at least {totalBytes} bytes observed).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        BytesObserved = totalBytes;
        return ManifestOperationResult<bool>.Success(true);
    }
}
