namespace Lunapack.Cli;

internal sealed record LifecycleHookAuthorization(
    IReadOnlyList<AuthorizedLifecycleHook> AuthorizedHooks,
    IReadOnlyList<PolicyDeniedLifecycleHook> DeniedScripts
);
