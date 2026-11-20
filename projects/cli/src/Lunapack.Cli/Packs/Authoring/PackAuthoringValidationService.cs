using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.ContentScreening;
using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.Authoring;

internal sealed class PackAuthoringValidationService(
    IFileSystem fileSystem,
    ExternalSourceRequirementPlanner requirementPlanner,
    ExternalSourceMaterializer materializer,
    PackInstallationPlanner installationPlanner,
    CliConsole console
)
{
    public async Task<ManifestOperationResult<bool>> ValidateExternalSourcesAsync(
        string projectDirectory,
        string packDirectory,
        PackManifest manifest,
        ResourceLimits? resourceLimits = null,
        bool allowHiddenCharacters = false
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
            projectDirectory,
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

        var limits = resourceLimits ?? ResourceLimits.Default;
        var materialization = await materializer.MaterializeAsync(
            projectDirectory,
            sourcePlan,
            limits
        );
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
                externalSources.Roots,
                limits
            );
            if (planned.Value is not { } installationPlan)
            {
                return ManifestOperationResult<bool>.Failure(
                    planned.Error ?? "Unable to validate managed-file selectors."
                );
            }

            var screening = ScreenManagedSources(
                packDirectory,
                installationPlan,
                allowHiddenCharacters
            );
            return screening.IsSuccess
                ? ManifestOperationResult<bool>.Success(true)
                : ManifestOperationResult<bool>.Failure(
                    screening.Error ?? PackContentScreeningPolicy.BlockingMessage
                );
        }
    }

    private ManifestOperationResult<bool> ScreenManagedSources(
        string packDirectory,
        PackInstallationPlan installationPlan,
        bool allowHiddenCharacters
    )
    {
        var findings = new List<PackContentFinding>();
        var omittedFindingCount = 0L;
        var hasHighConfidenceFinding = false;
        foreach (
            var managedFile in installationPlan
                .ManagedFiles.GroupBy(managedFile => managedFile.SourcePath, StringComparer.Ordinal)
                .Select(group => group.First())
        )
        {
            var source = ScreenManagedSource(packDirectory, managedFile);
            if (source.Value is not { } result)
            {
                return ManifestOperationResult<bool>.Failure(
                    source.Error
                        ?? $"Unable to screen managed source '{PackContentDiagnosticFormatter.EscapePath(managedFile.SourcePath)}'."
                );
            }

            findings.AddRange(result.Findings);
            omittedFindingCount += result.OmittedFindingCount;
            hasHighConfidenceFinding |= result.HasHighConfidenceFinding;
        }

        foreach (var finding in findings)
        {
            console.Warning(PackContentDiagnosticFormatter.Format(finding));
        }

        if (omittedFindingCount > 0)
        {
            console.Warning(PackContentDiagnosticFormatter.FormatOmitted(omittedFindingCount));
        }

        return PackContentScreeningPolicy.Blocks(hasHighConfidenceFinding, allowHiddenCharacters)
            ? ManifestOperationResult<bool>.Failure(PackContentScreeningPolicy.BlockingMessage)
            : ManifestOperationResult<bool>.Success(true);
    }

    private ManifestOperationResult<PackContentScanResult> ScreenManagedSource(
        string packDirectory,
        PlannedManagedFile managedFile
    )
    {
        try
        {
            var displayPath =
                managedFile.ExternalSource?.SourcePath
                ?? ProjectPath.Normalize(
                    fileSystem.Path.GetRelativePath(
                        managedFile.Pack.PackDirectory,
                        managedFile.SourcePath
                    )
                );
            return ManifestOperationResult<PackContentScanResult>.Success(
                PackContentScanner.Scan(
                    displayPath,
                    fileSystem.File.ReadAllBytes(managedFile.SourcePath)
                )
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var relativePath = ProjectPath.Normalize(
                fileSystem.Path.GetRelativePath(packDirectory, managedFile.SourcePath)
            );
            return ManifestOperationResult<PackContentScanResult>.Failure(
                $"Unable to read managed source '{PackContentDiagnosticFormatter.EscapePath(relativePath)}': {PackContentDiagnosticFormatter.EscapePath(exception.Message)}"
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
                (parameter.Value.Required is true || parameter.Value.RequiredWhen is not null)
                && parameter.Value.Default is null
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
