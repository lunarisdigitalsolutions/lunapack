namespace Lunapack.Cli;

internal sealed record ManagedRoot(
    ManagedRootOwner Owner,
    string SourceName,
    ConfiguredSourceIdentity? SourceIdentity,
    GitSourceProvenance? GitSource,
    IReadOnlyList<ManagedRootFile> Files
);
