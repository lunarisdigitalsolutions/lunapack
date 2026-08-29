using Lunapack.Cli.Project;

namespace Lunapack.Cli.Links;

internal sealed record BoundLinkSource(
    ProjectConfiguration.Source Source,
    ILinkSourceProvider Provider
);
