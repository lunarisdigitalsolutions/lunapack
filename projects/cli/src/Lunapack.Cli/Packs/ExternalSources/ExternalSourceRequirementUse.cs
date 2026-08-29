namespace Lunapack.Cli.Packs.ExternalSources;

internal sealed record ExternalSourceRequirementUse(
    string PackId,
    string PackVersion,
    string Alias,
    string? Description,
    int FileEntryCount
);
