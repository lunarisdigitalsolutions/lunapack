using System.Text;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Packs.Planning;

internal static class LinesManagedFileMerger
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
            var targetLines = ManagedFileMergeText.ReadLines(targetText);
            var knownLines = new HashSet<string>(targetLines, StringComparer.Ordinal);
            foreach (var sourceLine in ManagedFileMergeText.ReadLines(sourceText))
            {
                if (knownLines.Add(sourceLine))
                {
                    targetLines.Add(sourceLine);
                }
            }

            return ManifestOperationResult<byte[]>.Success(
                ManagedFileMergeText.CreateContents(
                    targetLines,
                    ManagedFileMergeText.HasTrailingNewline(targetText)
                        || ManagedFileMergeText.HasTrailingNewline(sourceText)
                )
            );
        }
        catch (DecoderFallbackException exception)
        {
            return ManifestOperationResult<byte[]>.Failure(
                $"Line merge requires UTF-8 text: {exception.Message}"
            );
        }
    }
}
