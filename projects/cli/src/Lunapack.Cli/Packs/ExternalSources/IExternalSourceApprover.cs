namespace Lunapack.Cli.Packs.ExternalSources;

internal interface IExternalSourceApprover
{
    Task<bool> ApproveAsync(
        IReadOnlyList<ExternalSourceRequirementGroup> sources,
        CancellationToken cancellationToken
    );
}
