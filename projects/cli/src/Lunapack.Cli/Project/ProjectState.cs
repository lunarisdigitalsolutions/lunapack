namespace Lunapack.Cli.Project;

internal sealed record ProjectState
{
    public required ProjectConfiguration Configuration { get; init; }

    public required ProjectLockFile LockFile { get; init; }
}
