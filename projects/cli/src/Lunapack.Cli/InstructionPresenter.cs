using System.Text;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class InstructionPresenter(CliConsole console)
{
    public ManifestOperationResult<bool> Present(string packId, PreparedInstruction instruction)
    {
        console.Info(string.Empty);
        console.Accent(
            $"Pack '{packId}' includes setup instructions. Follow these steps to finish:"
        );
        console.Info(string.Empty);

        if (instruction.Document.Introduction.Length > 0)
        {
            RenderMarkdown(instruction.Document.Introduction);
            console.Info(string.Empty);
        }

        foreach (var step in instruction.Document.Steps)
        {
            console.Render(new Markup($"[bold cyan]{Markup.Escape(FormatHeading(step))}[/]\n"));
            console.Info(string.Empty);
            if (step.Content.Length > 0)
            {
                RenderMarkdown(step.Content);
            }

            console.Info(string.Empty);
            if (console.IsInteractive && !console.WaitForContinue())
            {
                return ManifestOperationResult<bool>.Failure(
                    "Instruction presentation was cancelled."
                );
            }

            console.Info(string.Empty);
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private void RenderMarkdown(string markdown)
    {
        var inCodeBlock = false;
        foreach (var line in markdown.Trim('\r', '\n').ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            var content = inCodeBlock
                ? $"[grey]{Markup.Escape(line)}[/]"
                : FormatInlineMarkdown(line);
            console.Render(new Markup(content + Environment.NewLine));
        }
    }

    private static string FormatInlineMarkdown(string markdown)
    {
        var output = new StringBuilder(markdown.Length);
        for (var index = 0; index < markdown.Length; )
        {
            if (
                TryAppendLink(markdown, ref index, output)
                || TryAppendDelimited(markdown, ref index, output, "`", "yellow")
                || TryAppendDelimited(markdown, ref index, output, "**", "bold")
                || TryAppendDelimited(markdown, ref index, output, "*", "italic")
            )
            {
                continue;
            }

            output.Append(Markup.Escape(markdown[index].ToString()));
            index++;
        }

        return output.ToString();
    }

    private static bool TryAppendLink(string markdown, ref int index, StringBuilder output)
    {
        if (markdown[index] != '[')
        {
            return false;
        }

        var labelEnd = markdown.IndexOf("](", index + 1, StringComparison.Ordinal);
        var targetEnd = labelEnd < 0 ? -1 : markdown.IndexOf(')', labelEnd + 2);
        if (labelEnd < 0 || targetEnd < 0)
        {
            return false;
        }

        var label = markdown[(index + 1)..labelEnd];
        var target = markdown[(labelEnd + 2)..targetEnd];
        output.Append("[underline cyan]").Append(Markup.Escape(label)).Append("[/]");
        output.Append(" ([cyan]").Append(Markup.Escape(target)).Append("[/])");
        index = targetEnd + 1;
        return true;
    }

    private static bool TryAppendDelimited(
        string markdown,
        ref int index,
        StringBuilder output,
        string delimiter,
        string style
    )
    {
        if (!markdown.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal))
        {
            return false;
        }

        var valueStart = index + delimiter.Length;
        var valueEnd = markdown.IndexOf(delimiter, valueStart, StringComparison.Ordinal);
        if (valueEnd < valueStart)
        {
            return false;
        }

        output
            .Append('[')
            .Append(style)
            .Append(']')
            .Append(Markup.Escape(markdown[valueStart..valueEnd]))
            .Append("[/]");
        index = valueEnd + delimiter.Length;
        return true;
    }

    private static string FormatHeading(InstructionStep step)
    {
        var number = step.SubstepNumber is { } substepNumber
            ? $"{step.Number}.{substepNumber}"
            : step.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return step.Title is { Length: > 0 } title ? $"Step {number}: {title}" : $"Step {number}";
    }
}
