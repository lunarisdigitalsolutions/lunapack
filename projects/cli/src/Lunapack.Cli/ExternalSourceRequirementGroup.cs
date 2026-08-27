namespace Lunapack.Cli;

internal sealed record ExternalSourceRequirementGroup(
    SourceFingerprint Fingerprint,
    ProjectConfiguration.GitSource Source,
    IReadOnlyList<ExternalSourceRequirementUse> Uses,
    string WorkspaceSourceName,
    bool IsExisting,
    string? IdentifierConflict
)
{
    public int FileEntryCount => Uses.Sum(use => use.FileEntryCount);
}
