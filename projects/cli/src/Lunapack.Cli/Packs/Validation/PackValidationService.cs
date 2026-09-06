using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Project;
using NuGet.Versioning;

namespace Lunapack.Cli.Packs.Validation;

internal sealed class PackValidationService(
    IFileSystem fileSystem,
    ProjectStateStore projectStateStore,
    LocalPackDiscovery localPackDiscovery,
    PackCatalog packCatalog
)
{
    public async Task<ManifestOperationResult<LocalPackValidationResult>> ValidateAsync(
        string projectDirectory,
        string packId,
        string? version
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<LocalPackValidationResult>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        var foundCandidates = await FindCandidatesAsync(
            projectDirectory,
            state.Configuration,
            packId,
            version
        );
        if (foundCandidates.Value is not { } candidates)
        {
            return ManifestOperationResult<LocalPackValidationResult>.Failure(
                foundCandidates.Error ?? "Unable to validate local pack source."
            );
        }

        if (candidates.Count == 0)
        {
            return ManifestOperationResult<LocalPackValidationResult>.Failure(
                version is null
                    ? $"Pack '{packId}' is unavailable in configured sources."
                    : $"Pack '{packId}@{version}' is unavailable in configured sources."
            );
        }

        return ManifestOperationResult<LocalPackValidationResult>.Success(
            SelectCandidate(candidates, version).Result
        );
    }

    private async Task<ManifestOperationResult<List<PackValidationCandidate>>> FindCandidatesAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string packId,
        string? version
    )
    {
        var candidates = new List<PackValidationCandidate>();
        for (var sourceOrder = 0; sourceOrder < configuration.Sources.Count; sourceOrder++)
        {
            if (configuration.Sources[sourceOrder] is not ProjectConfiguration.LocalSource source)
            {
                continue;
            }

            var sourcePath = fileSystem.Path.GetFullPath(source.Path, projectDirectory);
            var validated = await localPackDiscovery.ValidateAsync(sourcePath);
            if (validated.Value is not { } results)
            {
                return ManifestOperationResult<List<PackValidationCandidate>>.Failure(
                    validated.Error ?? "Unable to validate local pack source."
                );
            }

            foreach (var result in results)
            {
                if (TryCreateCandidate(sourceOrder, result, packId, version, out var candidate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        if (configuration.Sources.Any(source => source is ProjectConfiguration.GitSource))
        {
            var browsed = await packCatalog.BrowseAsync(projectDirectory, configuration);
            if (browsed.Value is not { } catalog)
            {
                return ManifestOperationResult<List<PackValidationCandidate>>.Failure(
                    browsed.Error ?? "Unable to validate Git pack source."
                );
            }

            foreach (var pack in catalog.Where(pack => pack.GitSource is not null))
            {
                if (
                    string.Equals(pack.Manifest.Id, packId, StringComparison.Ordinal)
                    && (
                        version is null
                        || string.Equals(pack.Manifest.Version, version, StringComparison.Ordinal)
                    )
                )
                {
                    candidates.Add(
                        new PackValidationCandidate(
                            pack.SourceOrder,
                            new LocalPackValidationResult(
                                pack.RepositoryPath is { Length: > 0 } repositoryPath
                                    ? $"{repositoryPath}/pack.yml"
                                    : "pack.yml",
                                pack.Manifest,
                                []
                            ),
                            pack.Version
                        )
                    );
                }
            }
        }

        return ManifestOperationResult<List<PackValidationCandidate>>.Success(candidates);
    }

    private static bool TryCreateCandidate(
        int sourceOrder,
        LocalPackValidationResult result,
        string packId,
        string? requestedVersion,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PackValidationCandidate? candidate
    )
    {
        var manifest = result.Manifest;
        if (
            manifest is null
            || !string.Equals(manifest.Id, packId, StringComparison.Ordinal)
            || (
                requestedVersion is not null
                && !string.Equals(manifest.Version, requestedVersion, StringComparison.Ordinal)
            )
        )
        {
            candidate = null;
            return false;
        }

        candidate = new PackValidationCandidate(
            sourceOrder,
            result,
            NuGetVersion.TryParse(manifest.Version, out var parsedVersion)
                ? parsedVersion
                : new NuGetVersion(0, 0, 0)
        );
        return true;
    }

    private static PackValidationCandidate SelectCandidate(
        IReadOnlyList<PackValidationCandidate> candidates,
        string? requestedVersion
    ) =>
        requestedVersion is null
            ? candidates
                .OrderByDescending(candidate => candidate.Version, VersionComparer.VersionRelease)
                .ThenBy(candidate => candidate.SourceOrder)
                .First()
            : candidates.OrderBy(candidate => candidate.SourceOrder).First();

    private sealed record PackValidationCandidate(
        int SourceOrder,
        LocalPackValidationResult Result,
        NuGetVersion Version
    );
}
