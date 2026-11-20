using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;

namespace Lunapack.Cli.Catalog;

internal sealed class CatalogManifestBudget(ResourceLimits resourceLimits)
{
    public const long MaximumManifestBytes = 64L * 1024 * 1024;

    public long BytesObserved { get; private set; }

    public long RecordsObserved { get; private set; }

    public ManifestOperationResult<bool> Observe(CatalogPack pack) =>
        Observe(pack.ManifestByteCount);

    public ManifestOperationResult<bool> Observe(long manifestByteCount)
    {
        var observedRecords = checked(RecordsObserved + 1);
        if (observedRecords > resourceLimits.FilesPerGraph)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Pack catalog exceeds its {resourceLimits.FilesPerGraph} record limit (at least {observedRecords} manifests observed).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        ArgumentOutOfRangeException.ThrowIfNegative(manifestByteCount);

        var observedBytes = checked(BytesObserved + manifestByteCount);
        if (observedBytes > MaximumManifestBytes)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Pack catalog manifests exceed the {MaximumManifestBytes} byte aggregate limit (at least {observedBytes} bytes observed).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        RecordsObserved = observedRecords;
        BytesObserved = observedBytes;
        return ManifestOperationResult<bool>.Success(true);
    }

    public ManifestOperationResult<bool> Observe(IEnumerable<CatalogPack> packs)
    {
        foreach (var pack in packs)
        {
            var observed = Observe(pack);
            if (!observed.IsSuccess)
            {
                return observed;
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }
}
