namespace Lunapack.Cli;

internal sealed record ProjectState
{
    public required ProjectConfiguration Configuration { get; init; }

    public required ProjectLockFile LockFile { get; init; }
}
