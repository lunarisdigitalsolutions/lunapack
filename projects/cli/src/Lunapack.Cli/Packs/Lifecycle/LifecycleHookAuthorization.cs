namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record LifecycleHookAuthorization(
    IReadOnlyList<AuthorizedLifecycleHook> AuthorizedHooks,
    IReadOnlyList<PolicyDeniedLifecycleHook> DeniedScripts
);
