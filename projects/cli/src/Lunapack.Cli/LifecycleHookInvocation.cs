namespace Lunapack.Cli;

internal sealed record LifecycleHookInvocation(
    DiscoveredPack Pack,
    LifecycleHook Hook,
    PackManifest.PackHook Script,
    PackedHookFile? PackedFile,
    int Position = 1,
    PreparedInstruction? Instruction = null
)
{
    public bool IsScript => string.Equals(Script.Type, "script", StringComparison.Ordinal);

    public bool IsInstruction =>
        string.Equals(Script.Type, "instruction", StringComparison.Ordinal);

    public IReadOnlyList<string> Arguments =>
        PackedFile is { } packedFile
            ? [packedFile.CanonicalPath, .. Script.Arguments]
            : Script.Arguments;

    public string DeclaredExecutable => Script.Runner ?? Script.Command!;
}
