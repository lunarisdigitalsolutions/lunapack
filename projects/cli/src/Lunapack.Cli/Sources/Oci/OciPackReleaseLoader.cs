using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using NuGet.Versioning;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class OciPackReleaseLoader(
    IFileSystem fileSystem,
    IOciRegistryClient registryClient,
    OciSourceCredentialCoordinator credentialCoordinator,
    PackManifestStore packManifestStore
)
{
    public async Task<ManifestOperationResult<OciPackRelease?>> FetchAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string repository,
        string repositoryPackId,
        string tag,
        string workspace,
        string? expectedManifestDigest = null,
        string? expectedContentDigest = null,
        CancellationToken cancellationToken = default
    )
    {
        var context = CreateContext(projectDirectory, source);
        if (context.Value is not { } releaseContext)
        {
            return Failure<OciPackRelease?>(
                context.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        var artifact = await FetchArtifactAsync(
            projectDirectory,
            source,
            repository,
            tag,
            expectedManifestDigest,
            expectedContentDigest,
            releaseContext,
            cancellationToken
        );
        if (artifact.Value is not { } fetchedArtifact)
        {
            return artifact.IsSuccess
                ? ManifestOperationResult<OciPackRelease?>.Success(null)
                : Failure<OciPackRelease?>(
                    artifact.Error ?? $"Unable to fetch OCI release '{repository}:{tag}'."
                );
        }

        return await MaterializeAsync(
            projectDirectory,
            source,
            repository,
            repositoryPackId,
            tag,
            workspace,
            releaseContext,
            fetchedArtifact,
            cancellationToken
        );
    }

    private ManifestOperationResult<ReleaseContext> CreateContext(
        string projectDirectory,
        ProjectConfiguration.OciSource source
    )
    {
        var location = SourceIdentityNormalizer.NormalizeOciLocation(source.Url, source.Path);
        if (!location.IsSuccess)
        {
            return ManifestOperationResult<ReleaseContext>.Failure(
                location.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        var credentialIdentity = OciSourceCredentialIdentity.Create(
            fileSystem,
            projectDirectory,
            source
        );
        return credentialIdentity.Value is { } partitionId
            ? ManifestOperationResult<ReleaseContext>.Success(
                new ReleaseContext(new Uri(location.Value.Url), location.Value.Url, partitionId)
            )
            : ManifestOperationResult<ReleaseContext>.Failure(
                credentialIdentity.Error ?? $"OCI source '{source.Name}' is invalid."
            );
    }

    private async Task<ManifestOperationResult<RemoteOciArtifact?>> FetchArtifactAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string repository,
        string tag,
        string? expectedManifestDigest,
        string? expectedContentDigest,
        ReleaseContext context,
        CancellationToken cancellationToken
    )
    {
        var manifestReference = expectedManifestDigest ?? tag;
        var manifestResult = await credentialCoordinator.ExecuteAsync(
            projectDirectory,
            source,
            credential =>
                registryClient.FetchManifestAsync(
                    context.Registry,
                    repository,
                    manifestReference,
                    credential,
                    context.PartitionId,
                    cancellationToken
                ),
            cancellationToken
        );
        if (manifestResult.Value is not { } fetchedManifest)
        {
            return Failure<RemoteOciArtifact?>(
                FormatRegistryFailure(
                    source,
                    repository,
                    manifestResult.Failure,
                    "Unable to fetch OCI manifest."
                ),
                manifestResult.Failure?.Kind == OciRegistryFailureKind.NotFound
                    ? ManifestOperationErrorKind.PackNotFound
                    : ManifestOperationErrorKind.General
            );
        }

        if (
            expectedManifestDigest is not null
            && !string.Equals(
                fetchedManifest.Descriptor.Digest,
                expectedManifestDigest,
                StringComparison.Ordinal
            )
        )
        {
            return ManifestOperationResult<RemoteOciArtifact?>.Failure(
                $"OCI source '{source.Name}' returned a different manifest digest for '{repository}:{tag}'."
            );
        }

        if (!OciPackArtifact.IsLunaPackArtifact(fetchedManifest.Content))
        {
            return ManifestOperationResult<RemoteOciArtifact?>.Success(null);
        }

        var artifact = OciPackArtifact.ReadManifest(fetchedManifest.Content);
        if (artifact.Value is not { } metadata)
        {
            return ManifestOperationResult<RemoteOciArtifact?>.Failure(
                $"OCI source '{source.Name}' returned an invalid LunaPack manifest: {artifact.Error}"
            );
        }

        if (
            expectedContentDigest is not null
            && !string.Equals(
                metadata.Content.Digest,
                expectedContentDigest,
                StringComparison.Ordinal
            )
        )
        {
            return ManifestOperationResult<RemoteOciArtifact?>.Failure(
                $"OCI source '{source.Name}' returned a different content digest for '{repository}:{tag}'."
            );
        }

        return ManifestOperationResult<RemoteOciArtifact?>.Success(
            new RemoteOciArtifact(fetchedManifest, metadata)
        );
    }

    private async Task<ManifestOperationResult<OciPackRelease?>> MaterializeAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string repository,
        string repositoryPackId,
        string tag,
        string workspace,
        ReleaseContext context,
        RemoteOciArtifact artifact,
        CancellationToken cancellationToken
    )
    {
        var configResult = await FetchConfigAsync(
            projectDirectory,
            source,
            context.Registry,
            repository,
            context.PartitionId,
            artifact.Metadata,
            cancellationToken
        );
        if (configResult.Value is not { } config)
        {
            return Failure<OciPackRelease?>(
                configResult.Error ?? $"OCI source '{source.Name}' has invalid pack metadata."
            );
        }

        var releaseIdentity = OciPackArtifact.ValidateReleaseIdentity(
            config,
            repositoryPackId,
            tag
        );
        if (!releaseIdentity.IsSuccess)
        {
            return Failure<OciPackRelease?>(
                $"OCI source '{source.Name}' returned inconsistent pack identity: {releaseIdentity.Error}"
            );
        }

        var packDirectory = fileSystem.Path.Combine(workspace, "pack");
        var extraction = await FetchAndValidatePackAsync(
            projectDirectory,
            source,
            repository,
            artifact.Metadata.Content,
            workspace,
            packDirectory,
            context,
            config,
            cancellationToken
        );
        if (extraction.Value is not { } manifest)
        {
            return Failure<OciPackRelease?>(
                extraction.Error
                    ?? $"OCI source '{source.Name}' pack '{config.Id}@{config.Version}' is invalid."
            );
        }

        if (!NuGetVersion.TryParse(manifest.Version, out var version))
        {
            return Failure<OciPackRelease?>(
                $"OCI source '{source.Name}' returned an invalid version for pack '{config.Id}'."
            );
        }

        return ManifestOperationResult<OciPackRelease?>.Success(
            new OciPackRelease(
                manifest,
                version,
                packDirectory,
                new OciSourceProvenance
                {
                    RegistryUrl = context.RegistryUrl,
                    Repository = repository,
                    Tag = tag,
                    ManifestDigest = artifact.Manifest.Descriptor.Digest,
                    ContentDigest = artifact.Metadata.Content.Digest,
                    ContentSize = artifact.Metadata.Content.Size,
                }
            )
        );
    }

    private async Task<ManifestOperationResult<PackManifest>> FetchAndValidatePackAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string repository,
        OciRegistryDescriptor contentDescriptor,
        string workspace,
        string packDirectory,
        ReleaseContext context,
        OciPackArtifact.OciPackConfig config,
        CancellationToken cancellationToken
    )
    {
        var archivePath = fileSystem.Path.Combine(workspace, "pack.zip");
        var archiveResult = await FetchContentAsync(
            projectDirectory,
            source,
            context.Registry,
            repository,
            context.PartitionId,
            contentDescriptor,
            archivePath,
            cancellationToken
        );
        if (!archiveResult.IsSuccess)
        {
            return ManifestOperationResult<PackManifest>.Failure(
                archiveResult.Error ?? $"OCI source '{source.Name}' pack content is unavailable."
            );
        }

        return await ExtractAndValidateAsync(archivePath, packDirectory, config, cancellationToken);
    }

    private async Task<ManifestOperationResult<OciPackArtifact.OciPackConfig>> FetchConfigAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        Uri registry,
        string repository,
        string credentialPartition,
        OciPackArtifact.OciPackArtifactMetadata artifact,
        CancellationToken cancellationToken
    )
    {
        var descriptor = artifact.Config;
        using var buffer = new MemoryStream();
        var transfer = await credentialCoordinator.ExecuteAsync(
            projectDirectory,
            source,
            credential =>
            {
                buffer.SetLength(0);
                buffer.Position = 0;
                return registryClient.FetchBlobAsync(
                    registry,
                    repository,
                    descriptor,
                    buffer,
                    OciDistributionRegistryClient.MaximumMetadataBytes,
                    credential,
                    credentialPartition,
                    cancellationToken
                );
            },
            cancellationToken
        );
        if (transfer.Value is not { } completed)
        {
            return ManifestOperationResult<OciPackArtifact.OciPackConfig>.Failure(
                FormatRegistryFailure(
                    source,
                    repository,
                    transfer.Failure,
                    "Unable to fetch pack metadata."
                )
            );
        }

        if (
            completed.BytesWritten != descriptor.Size
            || !string.Equals(completed.Sha256Digest, descriptor.Digest, StringComparison.Ordinal)
        )
        {
            return ManifestOperationResult<OciPackArtifact.OciPackConfig>.Failure(
                "OCI pack metadata digest verification failed."
            );
        }

        return OciPackArtifact.ReadConfig(buffer.ToArray(), artifact);
    }

    private async Task<ManifestOperationResult<bool>> FetchContentAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        Uri registry,
        string repository,
        string credentialPartition,
        OciRegistryDescriptor descriptor,
        string archivePath,
        CancellationToken cancellationToken
    )
    {
        await using var archiveFile = fileSystem.File.Open(
            archivePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None
        );
        var transfer = await credentialCoordinator.ExecuteAsync(
            projectDirectory,
            source,
            credential =>
            {
                archiveFile.SetLength(0);
                archiveFile.Position = 0;
                return registryClient.FetchBlobAsync(
                    registry,
                    repository,
                    descriptor,
                    archiveFile,
                    OciPackArchive.MaximumArchiveBytes,
                    credential,
                    credentialPartition,
                    cancellationToken
                );
            },
            cancellationToken
        );
        await archiveFile.FlushAsync(cancellationToken);
        if (transfer.Value is not { } completed)
        {
            return ManifestOperationResult<bool>.Failure(
                FormatRegistryFailure(
                    source,
                    repository,
                    transfer.Failure,
                    "Unable to fetch OCI ZIP content."
                )
            );
        }

        return
            completed.BytesWritten == descriptor.Size
            && string.Equals(completed.Sha256Digest, descriptor.Digest, StringComparison.Ordinal)
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure("OCI ZIP digest verification failed.");
    }

    private async Task<ManifestOperationResult<PackManifest>> ExtractAndValidateAsync(
        string archivePath,
        string packDirectory,
        OciPackArtifact.OciPackConfig config,
        CancellationToken cancellationToken
    )
    {
        await using var archive = fileSystem.File.OpenRead(archivePath);
        var extraction = await OciPackArchive.ExtractAsync(
            fileSystem,
            archive,
            packDirectory,
            cancellationToken
        );
        if (extraction.Value is not { } files)
        {
            return ManifestOperationResult<PackManifest>.Failure(
                extraction.Error ?? "OCI ZIP archive could not be extracted safely."
            );
        }

        var loaded = await packManifestStore.LoadAsync(packDirectory);
        if (loaded.Value is not { } manifest)
        {
            return ManifestOperationResult<PackManifest>.Failure(
                loaded.Error ?? "OCI root pack.yml is invalid."
            );
        }

        if (
            !string.Equals(manifest.Id, config.Id, StringComparison.Ordinal)
            || !string.Equals(manifest.Version, config.Version, StringComparison.Ordinal)
        )
        {
            return ManifestOperationResult<PackManifest>.Failure(
                "Root pack.yml does not match OCI pack metadata."
            );
        }

        var issues = await PackManifestValidator.ValidateAsync(manifest, files);
        return issues.Count == 0
            ? ManifestOperationResult<PackManifest>.Success(manifest)
            : ManifestOperationResult<PackManifest>.Failure(string.Join(" ", issues));
    }

    private static string FormatRegistryFailure(
        ProjectConfiguration.OciSource source,
        string repository,
        OciRegistryFailure? failure,
        string fallback
    ) => $"OCI source '{source.Name}' repository '{repository}': {failure?.Message ?? fallback}";

    private static ManifestOperationResult<T> Failure<T>(
        string message,
        ManifestOperationErrorKind errorKind = ManifestOperationErrorKind.General
    ) => ManifestOperationResult<T>.Failure(message, errorKind);

    private sealed record ReleaseContext(Uri Registry, string RegistryUrl, string PartitionId);

    private sealed record RemoteOciArtifact(
        OciRegistryManifest Manifest,
        OciPackArtifact.OciPackArtifactMetadata Metadata
    );
}
