using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Publishing;

internal interface IPackPublisher
{
    bool CanPublish(ProjectConfiguration.Source source);

    Task<ManifestOperationResult<PackPublicationResult>> PublishAsync(
        string projectDirectory,
        string packDirectory,
        ProjectConfiguration.Source source,
        ResourceLimits? resourceLimits,
        bool allowHiddenCharacters = false,
        CancellationToken cancellationToken = default
    );
}
