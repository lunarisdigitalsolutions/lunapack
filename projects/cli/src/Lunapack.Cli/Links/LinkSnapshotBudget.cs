using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;

namespace Lunapack.Cli.Links;

internal sealed class LinkSnapshotBudget(ResourceLimits resourceLimits)
{
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    public long BytesObserved { get; private set; }

    public long EntriesObserved { get; private set; }

    public long FilesObserved { get; private set; }

    public long RemainingBytes => resourceLimits.PackSnapshotBytes - BytesObserved;

    public ManifestOperationResult<bool> AddFile(long byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

        var observedEntries = checked(EntriesObserved + 1);
        if (observedEntries > resourceLimits.FilesPerPack)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Link snapshot exceeds its {resourceLimits.FilesPerPack} file and directory limit (at least {observedEntries} entries selected).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var observedBytes = checked(BytesObserved + byteCount);
        if (observedBytes > resourceLimits.PackSnapshotBytes)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Link snapshot exceeds its {resourceLimits.PackSnapshotBytes} byte limit (at least {observedBytes} bytes observed).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        EntriesObserved = observedEntries;
        FilesObserved = checked(FilesObserved + 1);
        BytesObserved = observedBytes;
        return ManifestOperationResult<bool>.Success(true);
    }

    public ManifestOperationResult<bool> AddFilePath(string path, long byteCount)
    {
        var normalized = path.Replace('\\', '/');
        var separator = normalized.IndexOf('/');
        while (separator >= 0)
        {
            var directory = normalized[..separator];
            if (_directories.Add(directory))
            {
                var added = AddDirectory();
                if (!added.IsSuccess)
                {
                    return added;
                }
            }

            separator = normalized.IndexOf('/', separator + 1);
        }

        return AddFile(byteCount);
    }

    private ManifestOperationResult<bool> AddDirectory()
    {
        var observedEntries = checked(EntriesObserved + 1);
        if (observedEntries > resourceLimits.FilesPerPack)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Link snapshot exceeds its {resourceLimits.FilesPerPack} file and directory limit (at least {observedEntries} entries selected).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        EntriesObserved = observedEntries;
        return ManifestOperationResult<bool>.Success(true);
    }
}
