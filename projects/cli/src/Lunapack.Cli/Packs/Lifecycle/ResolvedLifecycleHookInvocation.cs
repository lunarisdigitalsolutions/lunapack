namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record ResolvedLifecycleHookInvocation(
    LifecycleHookInvocation Invocation,
    string Executable
)
{
    public IReadOnlyList<string> Arguments => Invocation.Arguments;
}
