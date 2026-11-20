namespace Lunapack.Cli.Trust;

internal sealed record LifecycleHookPolicy
{
    public const int MinimumTimeoutSeconds = 1;
    public const int MaximumTimeoutSeconds = 1800;

    public List<string> AllowedEnvironmentVariables { get; set; } = [];

    public int? TimeoutSeconds { get; set; }
}
