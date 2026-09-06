using System.Text;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Packs.Planning;

internal static class SectionManagedFileMerger
{
    public static ManifestOperationResult<byte[]> Merge(
        byte[] targetContents,
        byte[] sourceContents
    )
    {
        try
        {
            var targetText = ManagedFileMergeText.Decode(targetContents);
            var sourceText = ManagedFileMergeText.Decode(sourceContents);
            var sourceLines = ManagedFileMergeText.ReadLines(sourceText);
            if (sourceLines.Count < 2)
            {
                return ManifestOperationResult<byte[]>.Failure(
                    "Section merge requires distinct first and last source marker lines."
                );
            }

            var targetLines = ManagedFileMergeText.ReadLines(targetText);
            var firstMarkerIndexes = FindMarkerIndexes(targetLines, sourceLines[0]);
            var lastMarkerIndexes = FindMarkerIndexes(targetLines, sourceLines[^1]);
            if (firstMarkerIndexes.Count == 0 && lastMarkerIndexes.Count == 0)
            {
                targetLines.AddRange(sourceLines);
                return Success(targetLines, targetText, sourceText);
            }

            var markersAreIncompleteOrAmbiguous =
                firstMarkerIndexes.Count != 1
                || lastMarkerIndexes.Count != 1
                || firstMarkerIndexes[0] >= lastMarkerIndexes[0];
            if (markersAreIncompleteOrAmbiguous)
            {
                return ManifestOperationResult<byte[]>.Failure(
                    "Section merge markers are incomplete or ambiguous."
                );
            }

            var firstMarkerIndex = firstMarkerIndexes[0];
            targetLines.RemoveRange(firstMarkerIndex, lastMarkerIndexes[0] - firstMarkerIndex + 1);
            targetLines.InsertRange(firstMarkerIndex, sourceLines);
            return Success(targetLines, targetText, sourceText);
        }
        catch (DecoderFallbackException exception)
        {
            return ManifestOperationResult<byte[]>.Failure(
                $"Section merge requires UTF-8 text: {exception.Message}"
            );
        }
    }

    private static List<int> FindMarkerIndexes(IReadOnlyList<string> lines, string marker) =>
        [
            .. lines
                .Select((line, index) => (line, index))
                .Where(item => string.Equals(item.line, marker, StringComparison.Ordinal))
                .Select(item => item.index),
        ];

    private static ManifestOperationResult<byte[]> Success(
        IReadOnlyList<string> lines,
        string targetText,
        string sourceText
    ) =>
        ManifestOperationResult<byte[]>.Success(
            ManagedFileMergeText.CreateContents(
                lines,
                ManagedFileMergeText.HasTrailingNewline(targetText)
                    || ManagedFileMergeText.HasTrailingNewline(sourceText)
            )
        );
}
