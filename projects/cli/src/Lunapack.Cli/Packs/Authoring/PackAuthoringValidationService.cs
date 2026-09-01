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
        var request = new PackInstallationRequest(
            new PackReference(manifest.Id, manifest.Version),
            null,
            false
        );
        var parameters = PackParameterResolver.Resolve(graph, configuration, request);
        if (parameters.Value is not { } resolvedParameters)
        {
            return ManifestOperationResult<bool>.Failure(
                parameters.Error ?? "Unable to resolve validation parameters."
            );
        }

        var requirements = await requirementPlanner.PlanAsync(
            graph,
            configuration,
            resolvedParameters
        );
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
                request,
                resolvedParameters,
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

    private static ProjectConfiguration CreateValidationConfiguration(PackManifest manifest)
    {
        var variables = manifest
            .Parameters.Where(parameter =>
                parameter.Value.Required && parameter.Value.Default is null
            )
            .ToDictionary(
                parameter => parameter.Key,
                parameter => CreateValidationValue(parameter.Value),
                StringComparer.Ordinal
            );
        return new ProjectConfiguration
        {
            SchemaVersion = 1,
            Variables = variables,
            Packs =
            [
                new ProjectConfiguration.RequestedPack
                {
                    Id = manifest.Id,
                    Version = manifest.Version,
                },
            ],
        };

        static object CreateValidationValue(PackManifest.PackParameter parameter) =>
            parameter.Type switch
            {
                "bool" => false,
                "enum" when parameter.Multiple is true => new object[]
                {
                    parameter.Values?[0] ?? "validation",
                },
                "enum" => parameter.Values?[0] ?? "validation",
                _ => "validation",
            };
    }
}
