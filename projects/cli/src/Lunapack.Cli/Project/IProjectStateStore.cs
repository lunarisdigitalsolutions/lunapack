using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Project;

internal interface IProjectStateStore
{
    Task<ManifestOperationResult<ProjectState>> LoadAsync(string projectDirectory);

    Task<ManifestOperationResult<bool>> SaveAsync(string projectDirectory, ProjectState state);

    Task<ManifestOperationResult<bool>> SaveAllowingUnavailableSourcesAsync(
        string projectDirectory,
        ProjectState state
    );
}
