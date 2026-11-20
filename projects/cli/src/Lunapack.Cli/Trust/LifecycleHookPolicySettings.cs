namespace Lunapack.Cli.Trust;

internal sealed record LifecycleHookPolicySettings
{
    public LifecycleHookPolicy Global { get; set; } = new();

    public Dictionary<string, LifecycleHookPolicy> Projects { get; set; } =
        new(StringComparer.Ordinal);
}
