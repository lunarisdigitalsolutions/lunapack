using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Publishing;

internal sealed class PackPublishingService(
    IFileSystem fileSystem,
    IProjectStateStore projectStateStore,
    UserSettingsStore userSettingsStore,
    IReadOnlyList<IPackPublisher> publishers,
    ConfiguredSourceIgnoreService? configuredSourceIgnoreService = null
)
{
    private readonly ConfiguredSourceIgnoreService _sourceIgnoreService =
        configuredSourceIgnoreService
        ?? new ConfiguredSourceIgnoreService(fileSystem, userSettingsStore);

    public async Task<ManifestOperationResult<PackPublicationResult>> PublishAsync(
        string projectDirectory,
        string packDirectory,
        string targetName,
        CancellationToken cancellationToken = default
    )
    {
        var stateResult = await projectStateStore.LoadAsync(projectDirectory);
        if (stateResult.Value is not { } state)
        {
            return Failure(
                stateResult.Error ?? "Unable to load project state for pack publication."
            );
        }

        var source = state.Configuration.Sources.Find(configuredSource =>
            string.Equals(configuredSource.Name, targetName, StringComparison.Ordinal)
        );
        if (source is null)
        {
            return Failure($"Pack publishing target '{targetName}' is not configured.");
        }

        var ignoreContext = await _sourceIgnoreService.LoadAsync(projectDirectory);
        if (ignoreContext.Value is not { } context)
        {
            return Failure(ignoreContext.Error ?? "Unable to load local source ignore settings.");
        }

        var ignored = _sourceIgnoreService.IsIgnored(context, source);
        if (!ignored.IsSuccess)
        {
            return Failure(ignored.Error ?? $"Unable to identify source '{source.Name}'.");
        }

        if (ignored.Value)
        {
            return Failure(
                $"Pack publishing target '{source.Name}' is ignored locally. Run 'luna sources unignore {source.Name}' before publishing."
            );
        }

        var publisher = publishers.FirstOrDefault(candidate => candidate.CanPublish(source));
        if (publisher is null)
        {
            return Failure($"Pack source '{source.Name}' does not support publishing.");
        }

        return await publisher.PublishAsync(
            projectDirectory,
            packDirectory,
            source,
            cancellationToken
        );
    }

    private static ManifestOperationResult<PackPublicationResult> Failure(string error) =>
        ManifestOperationResult<PackPublicationResult>.Failure(error);
}
