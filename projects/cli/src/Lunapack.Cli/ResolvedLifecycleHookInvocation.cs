namespace Lunapack.Cli;

internal sealed record ResolvedLifecycleHookInvocation(
    LifecycleHookInvocation Invocation,
    string Executable
)
{
    public IReadOnlyList<string> Arguments => Invocation.Arguments;
}
