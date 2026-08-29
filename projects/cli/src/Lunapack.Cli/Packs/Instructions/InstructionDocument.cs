namespace Lunapack.Cli.Packs.Instructions;

internal sealed record InstructionDocument(
    string Introduction,
    IReadOnlyList<InstructionStep> Steps
);
