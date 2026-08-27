namespace Lunapack.Cli;

internal static class InstructionParser
{
    public static InstructionDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        content = RemoveTitle(content);

        var headings = FindHeadings(content);
        if (headings.Count == 0)
        {
            return new InstructionDocument(
                string.Empty,
                [new InstructionStep(1, null, null, content)]
            );
        }

        var steps = new List<InstructionStep>();
        var majorNumber = 0;
        var substepNumber = 0;
        var hasMajorHeading = false;
        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            int number;
            int? childNumber;
            if (heading.Level == 2)
            {
                majorNumber++;
                substepNumber = 0;
                hasMajorHeading = true;
                number = majorNumber;
                childNumber = null;
            }
            else if (hasMajorHeading)
            {
                substepNumber++;
                number = majorNumber;
                childNumber = substepNumber;
            }
            else
            {
                majorNumber++;
                number = majorNumber;
                childNumber = null;
            }

            var contentEnd =
                index + 1 < headings.Count ? headings[index + 1].Start : content.Length;
            steps.Add(
                new InstructionStep(
                    number,
                    childNumber,
                    heading.Title,
                    content[heading.ContentStart..contentEnd]
                )
            );
        }

        return new InstructionDocument(content[..headings[0].Start], steps);
    }

    private static string RemoveTitle(string content)
    {
        var lineEnd = content.IndexOf('\n');
        var firstLineEnd = lineEnd < 0 ? content.Length : lineEnd;
        var firstLine = content.AsSpan(0, firstLineEnd).TrimEnd('\r');
        return firstLine.StartsWith("# ", StringComparison.Ordinal)
            ? content[(lineEnd < 0 ? content.Length : lineEnd + 1)..].TrimStart('\r', '\n')
            : content;
    }

    private static List<InstructionHeading> FindHeadings(string content)
    {
        var headings = new List<InstructionHeading>();
        for (var lineStart = 0; lineStart < content.Length; )
        {
            var lineBreak = content.IndexOf('\n', lineStart);
            var lineEnd = lineBreak < 0 ? content.Length : lineBreak;
            var lineContentEnd =
                lineEnd > lineStart && content[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            var line = content.AsSpan(lineStart, lineContentEnd - lineStart);
            if (TryReadHeading(line, out var level, out var title))
            {
                headings.Add(
                    new InstructionHeading(
                        lineStart,
                        lineBreak < 0 ? content.Length : lineBreak + 1,
                        level,
                        title
                    )
                );
            }

            lineStart = lineBreak < 0 ? content.Length : lineBreak + 1;
        }

        return headings;
    }

    private static bool TryReadHeading(ReadOnlySpan<char> line, out int level, out string title)
    {
        if (line.StartsWith("### ", StringComparison.Ordinal))
        {
            level = 3;
            title = line[4..].ToString();
            return true;
        }

        if (line.StartsWith("## ", StringComparison.Ordinal))
        {
            level = 2;
            title = line[3..].ToString();
            return true;
        }

        level = 0;
        title = string.Empty;
        return false;
    }
}
