namespace Lunapack.Cli;

internal sealed class DenyExternalSourceApprover : IExternalSourceApprover
{
    public Task<bool> ApproveAsync(
        IReadOnlyList<ExternalSourceRequirementGroup> sources,
        CancellationToken cancellationToken
    ) => Task.FromResult(false);
}
