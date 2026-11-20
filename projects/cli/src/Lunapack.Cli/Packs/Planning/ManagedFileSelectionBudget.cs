using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;

namespace Lunapack.Cli.Packs.Planning;

internal sealed class ManagedFileSelectionBudget(ResourceLimits resourceLimits)
{
    private readonly Dictionary<string, long> _filesByPack = new(StringComparer.Ordinal);
    private long _graphFiles;

    public ManifestOperationResult<bool> Observe(string packId)
    {
        _filesByPack.TryGetValue(packId, out var packFiles);
        var observedPackFiles = checked(packFiles + 1);
        if (observedPackFiles > resourceLimits.FilesPerPack)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Pack '{packId}' selected file count exceeds its {resourceLimits.FilesPerPack} file limit (at least {observedPackFiles} files selected).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var observedGraphFiles = checked(_graphFiles + 1);
        if (observedGraphFiles > resourceLimits.FilesPerGraph)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Resolved pack graph selected file count exceeds its {resourceLimits.FilesPerGraph} file limit (at least {observedGraphFiles} files selected).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        _filesByPack[packId] = observedPackFiles;
        _graphFiles = observedGraphFiles;
        return ManifestOperationResult<bool>.Success(true);
    }
}
