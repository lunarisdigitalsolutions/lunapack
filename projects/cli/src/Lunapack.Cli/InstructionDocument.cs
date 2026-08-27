namespace Lunapack.Cli;

internal sealed record InstructionDocument(
    string Introduction,
    IReadOnlyList<InstructionStep> Steps
);
