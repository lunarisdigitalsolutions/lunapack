using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record ManagedRoot(
    ManagedRootOwner Owner,
    string SourceName,
    ConfiguredSourceIdentity? SourceIdentity,
    GitSourceProvenance? GitSource,
    IReadOnlyList<ManagedRootFile> Files
);
