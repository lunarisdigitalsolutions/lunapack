namespace Lunapack.Cli;

internal enum NextStepContext
{
    WorkspaceInitialized,
    SourceAdded,
    SourcesRemain,
    NoSourcesRemain,
    PacksDiscovered,
    PacksSearched,
    PackInspected,
    PackInstalled,
    PacksUpdated,
    PacksRemain,
    NoPacksRemain,
    MissingWorkspace,
    MissingSources,
    PackNotFound,
}
