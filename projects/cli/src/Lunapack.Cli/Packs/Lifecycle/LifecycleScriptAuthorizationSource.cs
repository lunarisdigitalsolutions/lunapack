using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record LifecycleScriptAuthorizationSource(
    LifecycleScriptAuthorizationKind Kind,
    IReadOnlyList<TrustScope> TrustScopes
)
{
    public static LifecycleScriptAuthorizationSource InvocationRun { get; } =
        new(LifecycleScriptAuthorizationKind.InvocationRun, []);

    public static LifecycleScriptAuthorizationSource InteractiveConfirmation { get; } =
        new(LifecycleScriptAuthorizationKind.InteractiveConfirmation, []);

    public static LifecycleScriptAuthorizationSource PersistedTrust(
        IReadOnlyList<TrustScope> trustScopes
    ) => new(LifecycleScriptAuthorizationKind.PersistedTrust, [.. trustScopes]);
}
