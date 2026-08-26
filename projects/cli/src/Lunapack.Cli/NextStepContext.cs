namespace Lunapack.Cli;

internal enum NextStepContext
{
    PackManifestMissing,
    PackManifestPresent,
    PackInitialized,
    PackModified,
    PackDisplayed,
    PackValidated,
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
    LinkAdded,
    LinkInstalled,
}
