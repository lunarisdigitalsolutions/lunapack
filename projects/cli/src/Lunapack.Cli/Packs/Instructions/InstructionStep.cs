namespace Lunapack.Cli.Packs.Instructions;

internal sealed record InstructionStep(
    int Number,
    int? SubstepNumber,
    string? Title,
    string Content
);
