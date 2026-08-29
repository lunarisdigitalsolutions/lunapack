using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Authoring;

internal sealed class PackAuthoringValidationService(
    ExternalSourceRequirementPlanner requirementPlanner,
    ExternalSourceMaterializer materializer,
    PackInstallationPlanner installationPlanner
)
{
    public async Task<ManifestOperationResult<bool>> ValidateExternalSourcesAsync(
        string packDirectory,
        PackManifest manifest
    )
    {
        var graph = CreateValidationGraph(packDirectory, manifest);
        var configuration = CreateValidationConfiguration(manifest);
        var parameters = new ResolvedPackParameters(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal),
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
        );
        var requirements = await requirementPlanner.PlanAsync(graph, configuration, parameters);
        if (requirements.Value is not { } sourcePlan)
        {
            return ManifestOperationResult<bool>.Failure(
                requirements.Error ?? "Unable to resolve external source requirements."
            );
        }

        var candidate = ExternalSourceConsentCoordinator.Preview(sourcePlan, configuration);
        if (candidate.Value is not { } approvedPlan)
        {
            return ManifestOperationResult<bool>.Failure(
                candidate.Error ?? "Unable to plan external source configuration."
            );
        }

        var materialization = await materializer.MaterializeAsync(sourcePlan);
        if (materialization.Value is not { } externalSources)
        {
            return ManifestOperationResult<bool>.Failure(
                materialization.Error ?? "Unable to materialize external sources."
            );
        }

        await using (externalSources)
        {
            var planned = installationPlanner.Plan(
                packDirectory,
                graph,
                new ProjectLockFile { SchemaVersion = 1 },
                approvedPlan.CandidateConfiguration,
                new PackInstallationRequest(
                    new PackReference(manifest.Id, manifest.Version),
                    null,
                    false
                ),
                parameters,
                externalSources.Roots
            );
            return planned.Value is not null
                ? ManifestOperationResult<bool>.Success(true)
                : ManifestOperationResult<bool>.Failure(
                    planned.Error ?? "Unable to validate managed-file selectors."
                );
        }
    }

    private static ResolvedPackGraph CreateValidationGraph(
        string packDirectory,
        PackManifest manifest
    )
    {
        var validationManifest = manifest with
        {
            ManagedFiles =
            [
                .. manifest.ManagedFiles.Select(managedFile =>
                    managedFile with
                    {
                        Condition = null,
                    }
                ),
            ],
        };
        return new ResolvedPackGraph(
            [new DiscoveredPack(packDirectory, packDirectory, validationManifest)],
            new HashSet<string>([manifest.Id], StringComparer.Ordinal)
        );
    }

    private static ProjectConfiguration CreateValidationConfiguration(PackManifest manifest) =>
        new()
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectConfiguration.RequestedPack
                {
                    Id = manifest.Id,
                    Version = manifest.Version,
                },
            ],
        };
}
