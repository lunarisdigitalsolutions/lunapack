namespace Lunapack.Cli;

internal interface IProjectStateStore
{
    Task<ManifestOperationResult<ProjectState>> LoadAsync(string projectDirectory);

    Task<ManifestOperationResult<bool>> SaveAsync(string projectDirectory, ProjectState state);
}
