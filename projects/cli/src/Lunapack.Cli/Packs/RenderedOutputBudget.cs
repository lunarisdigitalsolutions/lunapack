using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;

namespace Lunapack.Cli.Packs;

internal sealed class RenderedOutputBudget(ResourceLimits resourceLimits, long initialBytes = 0)
{
    public long BytesWritten { get; private set; } =
        initialBytes >= 0
            ? initialBytes
            : throw new ArgumentOutOfRangeException(nameof(initialBytes));

    public ManifestOperationResult<bool> Observe(
        string outputName,
        long bytesToAdd,
        long observedFileBytes
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytesToAdd);
        ArgumentOutOfRangeException.ThrowIfNegative(observedFileBytes);

        if (observedFileBytes > resourceLimits.RenderedFileBytes)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Rendered output '{outputName}' exceeds its {resourceLimits.RenderedFileBytes} byte file limit (at least {observedFileBytes} bytes produced).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var observedTotalBytes = checked(BytesWritten + bytesToAdd);
        if (observedTotalBytes > resourceLimits.RenderedTotalBytes)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Rendered output operation exceeds its {resourceLimits.RenderedTotalBytes} byte total limit (at least {observedTotalBytes} bytes produced).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        BytesWritten = observedTotalBytes;
        return ManifestOperationResult<bool>.Success(true);
    }
}
