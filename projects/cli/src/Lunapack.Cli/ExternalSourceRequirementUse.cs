namespace Lunapack.Cli;

internal sealed record ExternalSourceRequirementUse(
    string PackId,
    string PackVersion,
    string Alias,
    string? Description,
    int FileEntryCount
);
