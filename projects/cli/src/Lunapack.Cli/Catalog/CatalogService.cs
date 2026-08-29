using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Catalog;

internal sealed class CatalogService(PackCatalog packCatalog, ProjectStateStore projectStateStore)
{
    public async Task<ManifestOperationResult<ProjectConfiguration>> LoadConfigurationAsync(
        string projectDirectory
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        return loadedState.Value is { } state
            ? ManifestOperationResult<ProjectConfiguration>.Success(state.Configuration)
            : ManifestOperationResult<ProjectConfiguration>.Failure(
                loadedState.Error ?? "Unable to load project configuration."
            );
    }

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> LoadAsync(
        string projectDirectory
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        if (state.Configuration.Sources.Count == 0)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                "No sources are configured."
            );
        }

        return await packCatalog.BrowseAsync(projectDirectory, state.Configuration);
    }
}
