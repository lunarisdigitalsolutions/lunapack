namespace Lunapack.Cli.UnitTests;

internal sealed class FailingProjectStateStore(IProjectStateStore inner) : IProjectStateStore
{
    public Task<ManifestOperationResult<ProjectState>> LoadAsync(string projectDirectory) =>
        inner.LoadAsync(projectDirectory);

    public Task<ManifestOperationResult<bool>> SaveAsync(
        string projectDirectory,
        ProjectState state
    ) => Task.FromResult(ManifestOperationResult<bool>.Failure("Simulated state write failure."));

    public Task<ManifestOperationResult<bool>> SaveAllowingUnavailableSourcesAsync(
        string projectDirectory,
        ProjectState state
    ) => Task.FromResult(ManifestOperationResult<bool>.Failure("Simulated state write failure."));
}
