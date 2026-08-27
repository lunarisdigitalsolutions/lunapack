namespace Lunapack.Cli;

internal sealed record PreparedInstruction(
    PackedHookFile PackedFile,
    bool Templating,
    InstructionDocument Document
);
