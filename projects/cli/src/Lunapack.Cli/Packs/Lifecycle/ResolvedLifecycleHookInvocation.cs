namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record ResolvedLifecycleHookInvocation(
    LifecycleHookInvocation Invocation,
    string Executable,
    LifecycleScriptAuthorizationSource? Authorization = null
)
{
    public IReadOnlyList<string> Arguments => Invocation.Arguments;
}
