namespace Lunapack.Cli;

internal sealed record LifecycleHookInvocation(
    DiscoveredPack Pack,
    LifecycleHook Hook,
    PackManifest.LifecycleScript Script,
    PackedHookFile? PackedFile
)
{
    public IReadOnlyList<string> Arguments =>
        PackedFile is { } packedFile
            ? [packedFile.CanonicalPath, .. Script.Arguments]
            : Script.Arguments;

    public string DeclaredExecutable => Script.Runner ?? Script.Command!;
}
