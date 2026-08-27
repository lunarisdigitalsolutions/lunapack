namespace Lunapack.Cli;

internal sealed record ExternalSourceRequirementPlan(
    IReadOnlyList<ExternalSourceRequirementGroup> Groups,
    IReadOnlyList<ExternalSourceAliasMapping> Mappings
)
{
    public IReadOnlyList<ExternalSourceRequirementGroup> Existing =>
        [.. Groups.Where(group => group.IsExisting)];

    public IReadOnlyList<ExternalSourceRequirementGroup> Proposed =>
        [.. Groups.Where(group => !group.IsExisting)];

    public bool HasIdentifierConflicts =>
        Proposed.Any(group => group.IdentifierConflict is not null);
}
