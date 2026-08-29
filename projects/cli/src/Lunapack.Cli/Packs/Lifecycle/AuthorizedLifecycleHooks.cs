namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record AuthorizedLifecycleHooks(
    IReadOnlyList<AuthorizedLifecycleHook> PreMutation,
    IReadOnlyList<AuthorizedLifecycleHook> PostMutation
);
