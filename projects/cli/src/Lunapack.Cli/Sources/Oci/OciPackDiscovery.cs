using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using NuGet.Versioning;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class OciPackDiscovery(
    IFileSystem fileSystem,
    IOciRegistryClient registryClient,
    OciSourceCredentialCoordinator credentialCoordinator,
    OciPackReleaseLoader releaseLoader,
    CliConsole console
)
{
    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        int sourceOrder,
        CancellationToken cancellationToken = default
    )
    {
        var context = CreateContext(projectDirectory, source);
        if (context.Value is not { } discoveryContext)
        {
            return Failure<IReadOnlyList<CatalogPack>>(
                context.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        var repositories = await ListRepositoriesAsync(
            projectDirectory,
            source,
            discoveryContext,
            cancellationToken
        );
        if (repositories.Value is not { } repositoryNames)
        {
            return Failure<IReadOnlyList<CatalogPack>>(
                repositories.Error ?? $"Unable to list repositories for '{source.Name}'."
            );
        }

        var result = await DiscoverRepositoriesAsync(
            projectDirectory,
            source,
            sourceOrder,
            discoveryContext,
            repositoryNames,
            cancellationToken
        );
        if (result.HadFailures && result.Packs.Count == 0)
        {
            return Failure<IReadOnlyList<CatalogPack>>(
                $"OCI source '{source.Name}' could not provide any valid pack releases."
            );
        }

        if (result.HadFailures)
        {
            console.Warning(
                $"OCI source '{source.Name}' discovery is incomplete; some repositories or releases were unavailable."
            );
        }

        return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(result.Packs);
    }

    public static ManifestOperationResult<IReadOnlyList<CatalogPack>> BrowseCached(
        ProjectConfiguration.OciSource source
    )
    {
        return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
            $"OCI source '{source.Name}' was not checked offline; registry metadata is not cached."
        );
    }

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> ResolveCandidatesAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        int sourceOrder,
        string packId,
        string? requestedVersion,
        CancellationToken cancellationToken = default
    )
    {
        var repository = OciRepositoryTagMapper.CreateRepository(source, packId);
        if (repository.Value is not { } repositoryName)
        {
            return Failure<IReadOnlyList<CatalogPack>>(
                repository.Error ?? $"Pack '{packId}' cannot be represented in this OCI source."
            );
        }

        var context = CreateContext(projectDirectory, source);
        if (context.Value is not { } discoveryContext)
        {
            return Failure<IReadOnlyList<CatalogPack>>(
                context.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        if (requestedVersion is not null)
        {
            return await ResolveVersionAsync(
                projectDirectory,
                source,
                repositoryName,
                packId,
                requestedVersion,
                sourceOrder,
                discoveryContext,
                cancellationToken
            );
        }

        var tags = await ListTagsAsync(
            projectDirectory,
            source,
            repositoryName,
            discoveryContext,
            cancellationToken
        );
        if (tags.Value is not { } repositoryTags)
        {
            return tags.ErrorKind == ManifestOperationErrorKind.PackNotFound
                ? ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success([])
                : Failure<IReadOnlyList<CatalogPack>>(
                    tags.Error ?? $"Unable to list tags for '{repositoryName}'.",
                    tags.ErrorKind
                );
        }

        var result = await DiscoverReleaseTagsAsync(
            projectDirectory,
            source,
            repositoryName,
            packId,
            sourceOrder,
            discoveryContext,
            GetRecentTags(repositoryTags),
            cancellationToken
        );
        if (result.HadFailures && result.Packs.Count == 0)
        {
            return Failure<IReadOnlyList<CatalogPack>>(
                $"OCI source '{source.Name}' could not provide pack '{packId}'."
            );
        }

        if (result.HadFailures)
        {
            console.Warning(
                $"OCI source '{source.Name}' resolution for '{packId}' is incomplete; some releases were unavailable."
            );
        }

        return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(result.Packs);
    }

    private async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> ResolveVersionAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string repository,
        string packId,
        string requestedVersion,
        int sourceOrder,
        DiscoveryContext context,
        CancellationToken cancellationToken
    )
    {
        var tag = OciRepositoryTagMapper.CreateTag(requestedVersion);
        if (tag.Value is not { } versionTag)
        {
            return Failure<IReadOnlyList<CatalogPack>>(
                tag.Error ?? $"Version '{requestedVersion}' cannot be represented as an OCI tag."
            );
        }

        var candidate = await LoadCandidateAsync(
            projectDirectory,
            source,
            repository,
            packId,
            versionTag,
            sourceOrder,
            context,
            cancellationToken
        );
        if (candidate.Value is { } pack)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success([pack]);
        }

        if (candidate.ErrorKind == ManifestOperationErrorKind.PackNotFound)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success([]);
        }

        return Failure<IReadOnlyList<CatalogPack>>(
            candidate.Error
                ?? $"OCI reference '{repository}:{versionTag}' is not a LunaPack artifact.",
            candidate.ErrorKind
        );
    }

    private ManifestOperationResult<DiscoveryContext> CreateContext(
        string projectDirectory,
        ProjectConfiguration.OciSource source
    )
    {
        var location = SourceIdentityNormalizer.NormalizeOciLocation(source.Url, source.Path);
        if (!location.IsSuccess)
        {
            return ManifestOperationResult<DiscoveryContext>.Failure(
                location.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        var credentialIdentity = OciSourceCredentialIdentity.Create(
            fileSystem,
            projectDirectory,
            source
        );
        if (credentialIdentity.Value is not { } partitionId)
        {
            return ManifestOperationResult<DiscoveryContext>.Failure(
                credentialIdentity.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        return ManifestOperationResult<DiscoveryContext>.Success(
            new DiscoveryContext(
                new Uri(location.Value.Url),
                partitionId,
                ConfiguredSourceIdentity.Create(source),
                ProjectPath.NormalizeOptional(source.Path)?.Trim('/')
            )
        );
    }

    private async Task<ManifestOperationResult<IReadOnlyList<string>>> ListRepositoriesAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        DiscoveryContext context,
        CancellationToken cancellationToken
    )
    {
        var result = await credentialCoordinator.ExecuteAsync(
            projectDirectory,
            source,
            credential =>
                registryClient.ListRepositoriesAsync(
                    context.Registry,
                    credential,
                    context.PartitionId,
                    cancellationToken
                ),
            cancellationToken
        );
        return result.Value is { } repositories
            ? ManifestOperationResult<IReadOnlyList<string>>.Success(repositories)
            : ManifestOperationResult<IReadOnlyList<string>>.Failure(
                FormatRegistryFailure(
                    source,
                    result.Failure,
                    "Unable to enumerate OCI repositories."
                )
            );
    }

    private async Task<DiscoveryResult> DiscoverRepositoriesAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        int sourceOrder,
        DiscoveryContext context,
        IReadOnlyList<string> repositories,
        CancellationToken cancellationToken
    )
    {
        var packs = new List<CatalogPack>();
        var hadFailures = false;
        foreach (var repository in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsUnderConfiguredPath(repository, context.SourcePath))
            {
                continue;
            }

            var result = await DiscoverRepositoryAsync(
                projectDirectory,
                source,
                repository,
                sourceOrder,
                context,
                cancellationToken
            );
            packs.AddRange(result.Packs);
            hadFailures |= result.HadFailures;
        }

        return new DiscoveryResult(packs, hadFailures);
    }

    private async Task<DiscoveryResult> DiscoverRepositoryAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string repository,
        int sourceOrder,
        DiscoveryContext context,
        CancellationToken cancellationToken
    )
    {
        var packIdResult = OciRepositoryTagMapper.ReadPackId(repository, source.Path);
        if (packIdResult.Value is not { } packId)
        {
            console.Warning(
                $"Skipping OCI repository '{repository}' from source '{source.Name}': {packIdResult.Error}"
            );
            return new DiscoveryResult([], HadFailures: false);
        }

        var tags = await ListTagsAsync(
            projectDirectory,
            source,
            repository,
            context,
            cancellationToken
        );
        if (tags.Value is not { } repositoryTags)
        {
            if (tags.ErrorKind == ManifestOperationErrorKind.PackNotFound)
            {
                return new DiscoveryResult([], HadFailures: false);
            }

            console.Warning(
                FormatRepositoryFailure(
                    source,
                    repository,
                    tags.Error ?? "Unable to list OCI tags."
                )
            );
            return new DiscoveryResult([], HadFailures: true);
        }

        return await DiscoverReleaseTagsAsync(
            projectDirectory,
            source,
            repository,
            packId,
            sourceOrder,
            context,
            GetRecentTags(repositoryTags),
            cancellationToken
        );
    }

    private async Task<DiscoveryResult> DiscoverReleaseTagsAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string repository,
        string packId,
        int sourceOrder,
        DiscoveryContext context,
        IReadOnlyList<ReleaseTag> releaseTags,
        CancellationToken cancellationToken
    )
    {
        var packs = new List<CatalogPack>();
        var hadFailures = false;
        foreach (var releaseTag in releaseTags)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = await LoadCandidateAsync(
                projectDirectory,
                source,
                repository,
                packId,
                releaseTag.Tag,
                sourceOrder,
                context,
                cancellationToken
            );
            if (candidate.Value is { } pack)
            {
                packs.Add(pack);
            }
            else if (!candidate.IsSuccess)
            {
                if (candidate.ErrorKind == ManifestOperationErrorKind.PackNotFound)
                {
                    continue;
                }

                hadFailures = true;
                console.Warning(
                    FormatRepositoryFailure(
                        source,
                        repository,
                        candidate.Error ?? $"Unable to read tag '{releaseTag.Tag}'."
                    )
                );
            }
        }

        return new DiscoveryResult(packs, hadFailures);
    }

    private async Task<ManifestOperationResult<IReadOnlyList<string>>> ListTagsAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string repository,
        DiscoveryContext context,
        CancellationToken cancellationToken
    )
    {
        var result = await credentialCoordinator.ExecuteAsync(
            projectDirectory,
            source,
            credential =>
                registryClient.ListTagsAsync(
                    context.Registry,
                    repository,
                    credential,
                    context.PartitionId,
                    cancellationToken
                ),
            cancellationToken
        );
        return result.Value is { } tags
            ? ManifestOperationResult<IReadOnlyList<string>>.Success(tags)
            : ManifestOperationResult<IReadOnlyList<string>>.Failure(
                result.Failure?.Message ?? "Unable to list OCI tags.",
                result.Failure?.Kind == OciRegistryFailureKind.NotFound
                    ? ManifestOperationErrorKind.PackNotFound
                    : ManifestOperationErrorKind.General
            );
    }

    private async Task<ManifestOperationResult<CatalogPack?>> LoadCandidateAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string repository,
        string packId,
        string tag,
        int sourceOrder,
        DiscoveryContext context,
        CancellationToken cancellationToken
    )
    {
        var temporaryWorkspace = OciTemporaryWorkspace.Create(fileSystem);
        if (temporaryWorkspace.Value is not { } workspace)
        {
            return ManifestOperationResult<CatalogPack?>.Failure(
                temporaryWorkspace.Error ?? "Unable to create an OCI workspace."
            );
        }

        using (workspace)
        {
            var release = await releaseLoader.FetchAsync(
                projectDirectory,
                source,
                repository,
                packId,
                tag,
                workspace.Path,
                cancellationToken: cancellationToken
            );
            if (release.Value is not { } packRelease)
            {
                return release.IsSuccess
                    ? ManifestOperationResult<CatalogPack?>.Success(null)
                    : ManifestOperationResult<CatalogPack?>.Failure(
                        release.Error ?? $"Unable to read OCI release '{repository}:{tag}'.",
                        release.ErrorKind
                    );
            }

            return ManifestOperationResult<CatalogPack?>.Success(
                new CatalogPack(
                    string.Empty,
                    string.Empty,
                    sourceOrder,
                    packRelease.Manifest,
                    packRelease.Version,
                    source.Name,
                    context.SourceIdentity,
                    RepositoryPath: repository,
                    OciSource: packRelease.Provenance
                )
            );
        }
    }

    private static IReadOnlyList<ReleaseTag> GetRecentTags(IReadOnlyList<string> tags)
    {
        var versions = new List<ReleaseTag>();
        var seenTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (!seenTags.Add(tag))
            {
                continue;
            }

            var versionResult = OciRepositoryTagMapper.ReadVersion(tag);
            if (
                versionResult.Value is { } version
                && NuGetVersion.TryParse(version, out var parsedVersion)
            )
            {
                versions.Add(new ReleaseTag(tag, parsedVersion));
            }
        }

        return
        [
            .. versions
                .OrderByDescending(release => release.Version, VersionComparer.VersionRelease)
                .ThenBy(release => release.Tag, StringComparer.Ordinal)
                .Take(PackCatalog.MaximumVersionCount),
        ];
    }

    private static bool IsUnderConfiguredPath(string repository, string? sourcePath) =>
        sourcePath is null || repository.StartsWith($"{sourcePath}/", StringComparison.Ordinal);

    private static string FormatRegistryFailure(
        ProjectConfiguration.OciSource source,
        OciRegistryFailure? failure,
        string fallback
    ) => $"OCI source '{source.Name}': {failure?.Message ?? fallback}";

    private static string FormatRepositoryFailure(
        ProjectConfiguration.OciSource source,
        string repository,
        string error
    ) => $"OCI source '{source.Name}' repository '{repository}': {error}";

    private static ManifestOperationResult<T> Failure<T>(
        string message,
        ManifestOperationErrorKind errorKind = ManifestOperationErrorKind.General
    ) => ManifestOperationResult<T>.Failure(message, errorKind);

    private sealed record DiscoveryContext(
        Uri Registry,
        string PartitionId,
        ConfiguredSourceIdentity SourceIdentity,
        string? SourcePath
    );

    private sealed record DiscoveryResult(List<CatalogPack> Packs, bool HadFailures);

    private sealed record ReleaseTag(string Tag, NuGetVersion Version);
}
