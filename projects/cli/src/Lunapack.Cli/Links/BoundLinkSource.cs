namespace Lunapack.Cli;

internal sealed record BoundLinkSource(
    ProjectConfiguration.Source Source,
    ILinkSourceProvider Provider
);
