using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.ExternalSources;

internal sealed class ExternalSourceConsentCoordinator(
    IExternalSourceApprover approver,
    IExternalSourceIdentifierPrompter identifierPrompter
)
{
    public async Task<ManifestOperationResult<ApprovedExternalSourcePlan>> ApproveAsync(
        ExternalSourceRequirementPlan plan,
        ProjectConfiguration configuration,
        bool acceptSources,
        CancellationToken cancellationToken = default
    )
    {
        var resolvedPlan = await ResolveConflictsAsync(
            plan,
            configuration,
            acceptSources,
            cancellationToken
        );
        if (resolvedPlan.Value is not { } requirements)
        {
            return ManifestOperationResult<ApprovedExternalSourcePlan>.Failure(
                resolvedPlan.Error ?? "Unable to resolve external source identifiers."
            );
        }

        if (
            requirements.Proposed.Count > 0
            && !acceptSources
            && !await approver.ApproveAsync(requirements.Proposed, cancellationToken)
        )
        {
            return ManifestOperationResult<ApprovedExternalSourcePlan>.Failure(
                "External source approval was declined."
            );
        }

        return CreateCandidate(requirements, configuration);
    }

    public static ManifestOperationResult<ApprovedExternalSourcePlan> Preview(
        ExternalSourceRequirementPlan plan,
        ProjectConfiguration configuration
    )
    {
        var conflict = plan.Proposed.FirstOrDefault(group => group.IdentifierConflict is not null);
        return conflict is null
            ? CreateCandidate(plan, configuration)
            : ManifestOperationResult<ApprovedExternalSourcePlan>.Failure(
                $"Source identifier '{conflict.WorkspaceSourceName}' is already in use. Configure '{conflict.Fingerprint.Identity}' explicitly with 'luna sources add git <name> <repository-url> --ref {conflict.Fingerprint.Ref}'."
            );
    }

    private static ManifestOperationResult<ApprovedExternalSourcePlan> CreateCandidate(
        ExternalSourceRequirementPlan requirements,
        ProjectConfiguration configuration
    )
    {
        var candidate = configuration with
        {
            Sources =
            [
                .. configuration.Sources,
                .. requirements.Proposed.Select(group =>
                    group.Source with
                    {
                        Name = group.WorkspaceSourceName,
                    }
                ),
            ],
        };
        var issues = ManifestModelValidator.Validate(candidate);
        return issues.Count == 0
            ? ManifestOperationResult<ApprovedExternalSourcePlan>.Success(
                new ApprovedExternalSourcePlan(requirements, candidate)
            )
            : ManifestOperationResult<ApprovedExternalSourcePlan>.Failure(
                $"Proposed source configuration is invalid: {string.Join("; ", issues)}"
            );
    }

    private async Task<
        ManifestOperationResult<ExternalSourceRequirementPlan>
    > ResolveConflictsAsync(
        ExternalSourceRequirementPlan plan,
        ProjectConfiguration configuration,
        bool acceptSources,
        CancellationToken cancellationToken
    )
    {
        var names = configuration
            .Sources.Select(source => source.Name)
            .Concat(
                plan.Proposed.Where(group => group.IdentifierConflict is null)
                    .Select(group => group.WorkspaceSourceName)
            )
            .ToHashSet(StringComparer.Ordinal);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        var groups = new List<ExternalSourceRequirementGroup>(plan.Groups.Count);
        foreach (var group in plan.Groups)
        {
            if (group.IdentifierConflict is null)
            {
                groups.Add(group);
                continue;
            }

            var resolvedIdentifier = await ResolveIdentifierAsync(
                group,
                names,
                acceptSources,
                cancellationToken
            );
            if (resolvedIdentifier.Value is not { } identifier)
            {
                return ManifestOperationResult<ExternalSourceRequirementPlan>.Failure(
                    resolvedIdentifier.Error
                        ?? "External source identifier selection was cancelled."
                );
            }

            replacements[group.Fingerprint.Value] = identifier;
            groups.Add(
                group with
                {
                    Source = group.Source with { Name = identifier },
                    WorkspaceSourceName = identifier,
                    IdentifierConflict = null,
                }
            );
        }

        var mappings = plan
            .Mappings.Select(mapping =>
                replacements.TryGetValue(mapping.Fingerprint.Value, out var identifier)
                    ? mapping with
                    {
                        WorkspaceSourceName = identifier,
                    }
                    : mapping
            )
            .ToArray();
        return ManifestOperationResult<ExternalSourceRequirementPlan>.Success(
            new ExternalSourceRequirementPlan(groups, mappings)
        );
    }

    private async Task<ManifestOperationResult<string>> ResolveIdentifierAsync(
        ExternalSourceRequirementGroup group,
        HashSet<string> names,
        bool acceptSources,
        CancellationToken cancellationToken
    )
    {
        if (acceptSources)
        {
            return ManifestOperationResult<string>.Failure(
                $"Source identifier '{group.WorkspaceSourceName}' is already in use. Configure '{group.Fingerprint.Identity}' explicitly with 'luna sources add git <name> <repository-url> --ref {group.Fingerprint.Ref}'."
            );
        }

        string? identifier;
        do
        {
            identifier = await identifierPrompter.PromptAsync(
                group,
                group.WorkspaceSourceName,
                cancellationToken
            );
            if (identifier is null)
            {
                return ManifestOperationResult<string>.Failure(
                    "External source identifier selection was cancelled."
                );
            }
        } while (!ManifestModelValidator.IsSourceAlias(identifier) || !names.Add(identifier));

        return ManifestOperationResult<string>.Success(identifier);
    }
}
