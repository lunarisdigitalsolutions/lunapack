namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record EffectiveLifecycleHookPolicy(
    IReadOnlySet<string> AllowedEnvironmentVariables,
    int TimeoutSeconds
);
