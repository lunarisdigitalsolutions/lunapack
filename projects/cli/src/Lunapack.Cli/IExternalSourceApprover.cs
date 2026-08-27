namespace Lunapack.Cli;

internal interface IExternalSourceApprover
{
    Task<bool> ApproveAsync(
        IReadOnlyList<ExternalSourceRequirementGroup> sources,
        CancellationToken cancellationToken
    );
}
