namespace Lunapack.Cli;

internal sealed record InstructionStep(
    int Number,
    int? SubstepNumber,
    string? Title,
    string Content
);
