namespace Lunapack.Cli;

internal sealed record AuthorizedLifecycleHooks(
    IReadOnlyList<AuthorizedLifecycleHook> PreMutation,
    IReadOnlyList<AuthorizedLifecycleHook> PostMutation
);
