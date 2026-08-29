using Lunapack.Cli.Packs.Lifecycle;

namespace Lunapack.Cli.Packs.Instructions;

internal sealed record PreparedInstruction(
    PackedHookFile PackedFile,
    bool Templating,
    InstructionDocument Document
);
