using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Links;

internal interface ILinkSourceProvider
{
    bool CanProvide(ProjectConfiguration.Source source);

    Task<ManifestOperationResult<LinkSourceListing>> ListAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        ProjectConfiguration.Link link,
        ConfiguredSourceIdentity? lockedIdentity,
        ResourceLimits? resourceLimits = null,
        CancellationToken cancellationToken = default
    );

    Task<ManifestOperationResult<IReadOnlyDictionary<string, string>>> MaterializeAsync(
        LinkSourceListing listing,
        IReadOnlyList<string> selectedPaths,
        LinkOperationWorkspace workspace,
        ResourceLimits? resourceLimits = null,
        CancellationToken cancellationToken = default
    );
}
