using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests;

internal sealed class FailingProjectStateStore(IProjectStateStore inner) : IProjectStateStore
{
    public Task<ManifestOperationResult<ProjectState>> LoadAsync(string projectDirectory) =>
        inner.LoadAsync(projectDirectory);

    public Task<ManifestOperationResult<bool>> ValidateForSaveAsync(
        string projectDirectory,
        ProjectState state
    ) => inner.ValidateForSaveAsync(projectDirectory, state);

    public Task<ManifestOperationResult<bool>> ValidateForSaveAllowingUnavailableSourcesAsync(
        string projectDirectory,
        ProjectState state
    ) => inner.ValidateForSaveAllowingUnavailableSourcesAsync(projectDirectory, state);

    public Task<ManifestOperationResult<bool>> SaveAsync(
        string projectDirectory,
        ProjectState state
    ) => Task.FromResult(ManifestOperationResult<bool>.Failure("Simulated state write failure."));

    public Task<ManifestOperationResult<bool>> SaveAllowingUnavailableSourcesAsync(
        string projectDirectory,
        ProjectState state
    ) => Task.FromResult(ManifestOperationResult<bool>.Failure("Simulated state write failure."));
}
