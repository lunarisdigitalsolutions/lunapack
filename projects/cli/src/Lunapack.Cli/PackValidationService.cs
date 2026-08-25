using System.IO.Abstractions;
using NuGet.Versioning;

namespace Lunapack.Cli;

internal sealed class PackValidationService(
    IFileSystem fileSystem,
    ProjectStateStore projectStateStore,
    LocalPackDiscovery localPackDiscovery
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
                    ? $"Pack '{packId}' is unavailable in configured local sources."
                    : $"Pack '{packId}@{version}' is unavailable in configured local sources."
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

        return ManifestOperationResult<List<PackValidationCandidate>>.Success(candidates);
    }

    private static bool TryCreateCandidate(
        int sourceOrder,
        LocalPackValidationResult result,
        string packId,
        string? requestedVersion,
        out PackValidationCandidate candidate
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
            candidate = default!;
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
