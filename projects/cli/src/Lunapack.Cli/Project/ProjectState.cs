using Lunapack.Cli.Application.Preferences;

namespace Lunapack.Cli.Project;

internal sealed record ProjectState
{
    public required ProjectConfiguration Configuration { get; init; }

    public ResourceLimits EffectiveResourceLimits { get; init; } = ResourceLimits.Default;

    public required ProjectLockFile LockFile { get; init; }
}
