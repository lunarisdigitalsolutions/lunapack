using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Publishing;

internal interface IPackPublisher
{
    bool CanPublish(ProjectConfiguration.Source source);

    Task<ManifestOperationResult<PackPublicationResult>> PublishAsync(
        string projectDirectory,
        string packDirectory,
        ProjectConfiguration.Source source,
        CancellationToken cancellationToken
    );
}
