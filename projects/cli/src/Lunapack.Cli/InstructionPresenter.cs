namespace Lunapack.Cli;

internal sealed class InstructionPresenter(CliConsole console)
{
    public ManifestOperationResult<bool> Present(PreparedInstruction instruction)
    {
        if (instruction.Document.Introduction.Length > 0)
        {
            console.Info(instruction.Document.Introduction);
        }

        foreach (var step in instruction.Document.Steps)
        {
            console.Info(FormatHeading(step));
            if (step.Content.Length > 0)
            {
                console.Info(step.Content);
            }

            if (console.IsInteractive && !console.WaitForContinue())
            {
                return ManifestOperationResult<bool>.Failure(
                    "Instruction presentation was cancelled."
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private static string FormatHeading(InstructionStep step)
    {
        var number = step.SubstepNumber is { } substepNumber
            ? $"{step.Number}.{substepNumber}"
            : step.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return step.Title is { Length: > 0 } title ? $"Step {number}: {title}" : $"Step {number}";
    }
}
