using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.Authoring;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Packs.Publishing;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class OciPackPublisher(
    IFileSystem fileSystem,
    IOciRegistryClient registryClient,
    OciSourceCredentialCoordinator credentialCoordinator,
    PackManifestStore packManifestStore,
    PackAuthoringValidationService authoringValidationService,
    IGitProcessRunner gitProcessRunner,
    IOperationSnapshotSecurity? snapshotSecurity = null
) : IPackPublisher
{
    private static readonly TimeSpan _sourceMetadataTimeout = TimeSpan.FromSeconds(5);

    public bool CanPublish(ProjectConfiguration.Source source) =>
        source is ProjectConfiguration.OciSource;

    public async Task<ManifestOperationResult<PackPublicationResult>> PublishAsync(
        string projectDirectory,
        string packDirectory,
        ProjectConfiguration.Source source,
        CancellationToken cancellationToken
    )
    {
        if (source is not ProjectConfiguration.OciSource ociSource)
        {
            return Failure<PackPublicationResult>(
                $"Pack source '{source.Name}' does not support OCI publishing."
            );
        }

        var workspaceResult = OciTemporaryWorkspace.Create(fileSystem, snapshotSecurity);
        if (workspaceResult.Value is not { } workspace)
        {
            return Failure<PackPublicationResult>(
                workspaceResult.Error ?? "Unable to create a private OCI publish workspace."
            );
        }

        using (workspace)
        {
            var archivePath = fileSystem.Path.Combine(workspace.Path, "pack.zip");
            var archive = await CreateArchiveAsync(packDirectory, archivePath, cancellationToken);
            if (archive.Value is not { } content)
            {
                return Failure<PackPublicationResult>(
                    archive.Error ?? "Unable to create the OCI pack archive."
                );
            }

            var snapshotDirectory = fileSystem.Path.Combine(workspace.Path, "pack");
            var validation = await ValidateArchiveAsync(
                projectDirectory,
                archivePath,
                snapshotDirectory,
                cancellationToken
            );
            if (validation.Value is not { } manifest)
            {
                return Failure<PackPublicationResult>(
                    validation.Error ?? "The pack is not valid for publication."
                );
            }

            var sourceMetadata = await ReadSourceMetadataAsync(packDirectory, cancellationToken);
            return await PublishValidatedPackAsync(
                projectDirectory,
                ociSource,
                manifest,
                archivePath,
                content,
                sourceMetadata,
                cancellationToken
            );
        }
    }

    private async Task<ManifestOperationResult<OciBlobTransfer>> CreateArchiveAsync(
        string packDirectory,
        string archivePath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var archive = fileSystem.File.Open(
                archivePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None
            );
            var created = await OciPackArchive.CreateAsync(
                fileSystem,
                packDirectory,
                archive,
                cancellationToken
            );
            await archive.FlushAsync(cancellationToken);
            return created;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<OciBlobTransfer>.Failure(
                $"Unable to create the OCI pack archive: {exception.Message}"
            );
        }
    }

    private async Task<ManifestOperationResult<PackManifest>> ValidateArchiveAsync(
        string projectDirectory,
        string archivePath,
        string snapshotDirectory,
        CancellationToken cancellationToken
    )
    {
        ManifestOperationResult<IReadOnlyList<string>> extraction;
        try
        {
            await using var archive = fileSystem.File.OpenRead(archivePath);
            extraction = await OciPackArchive.ExtractAsync(
                fileSystem,
                archive,
                snapshotDirectory,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<PackManifest>.Failure(
                $"Unable to validate the OCI pack archive: {exception.Message}"
            );
        }

        if (extraction.Value is not { } sourceFiles)
        {
            return ManifestOperationResult<PackManifest>.Failure(
                extraction.Error ?? "The OCI pack archive contains unsafe content."
            );
        }

        var loadedManifest = await packManifestStore.LoadAsync(snapshotDirectory);
        if (loadedManifest.Value is not { } manifest)
        {
            return ManifestOperationResult<PackManifest>.Failure(
                loadedManifest.Error ?? "The archive does not contain a valid root pack.yml."
            );
        }

        var issues = await PackManifestValidator.ValidateAsync(manifest, sourceFiles);
        if (issues.Count > 0)
        {
            return ManifestOperationResult<PackManifest>.Failure(
                $"Invalid pack content: {string.Join(" ", issues)}"
            );
        }

        var externalValidation = await authoringValidationService.ValidateExternalSourcesAsync(
            snapshotDirectory,
            manifest
        );
        return externalValidation.IsSuccess
            ? ManifestOperationResult<PackManifest>.Success(manifest)
            : ManifestOperationResult<PackManifest>.Failure(
                externalValidation.Error ?? "Pack external-source validation failed."
            );
    }

    private async Task<ManifestOperationResult<PackPublicationResult>> PublishValidatedPackAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        PackManifest pack,
        string archivePath,
        OciBlobTransfer content,
        (string? Source, string? Revision) sourceMetadata,
        CancellationToken cancellationToken
    )
    {
        var targetResult = CreateTarget(fileSystem, projectDirectory, source, pack);
        if (targetResult.Value is not { } target)
        {
            return Failure<PackPublicationResult>(
                targetResult.Error ?? $"Pack '{pack.Id}@{pack.Version}' has an invalid OCI target."
            );
        }

        var available = await EnsureTagIsAvailableAsync(
            projectDirectory,
            source,
            target.Context,
            target.Repository,
            target.Tag,
            cancellationToken
        );
        if (!available.IsSuccess)
        {
            return Failure<PackPublicationResult>(
                available.Error ?? $"OCI tag '{target.Repository}:{target.Tag}' is unavailable."
            );
        }

        var artifactResult = CreateArtifact(pack, content, sourceMetadata);
        if (artifactResult.Value is not { } artifact)
        {
            return Failure<PackPublicationResult>(
                artifactResult.Error ?? "Unable to create a valid LunaPack OCI manifest."
            );
        }

        var upload = await PushArtifactAsync(
            projectDirectory,
            source,
            target,
            archivePath,
            artifact,
            cancellationToken
        );
        if (!upload.IsSuccess)
        {
            return Failure<PackPublicationResult>(
                upload.Error ?? $"Unable to publish OCI release '{target.Repository}:{target.Tag}'."
            );
        }

        return await VerifyPublicationAsync(
            projectDirectory,
            source,
            pack,
            target,
            artifact.ManifestDescriptor,
            cancellationToken
        );
    }

    private static ManifestOperationResult<PublishTarget> CreateTarget(
        IFileSystem fileSystem,
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        PackManifest pack
    )
    {
        var context = CreateContext(fileSystem, projectDirectory, source);
        if (context.Value is not { } publishContext)
        {
            return ManifestOperationResult<PublishTarget>.Failure(
                context.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        var repository = OciRepositoryTagMapper.CreateRepository(source, pack.Id);
        var tag = OciRepositoryTagMapper.CreateTag(pack.Version);
        return repository.Value is { } repositoryName && tag.Value is { } versionTag
            ? ManifestOperationResult<PublishTarget>.Success(
                new PublishTarget(publishContext, repositoryName, versionTag)
            )
            : ManifestOperationResult<PublishTarget>.Failure(
                repository.Error
                    ?? tag.Error
                    ?? $"Pack '{pack.Id}@{pack.Version}' cannot be represented as an OCI reference."
            );
    }

    private static ManifestOperationResult<PreparedArtifact> CreateArtifact(
        PackManifest pack,
        OciBlobTransfer content,
        (string? Source, string? Revision) sourceMetadata
    )
    {
        var (configContent, configDescriptor) = OciPackArtifact.CreateConfig(pack);
        var contentDescriptor = new OciRegistryDescriptor(
            OciPackMediaTypes.ContentZip,
            content.Sha256Digest,
            content.BytesWritten
        );
        var manifest = OciPackArtifact.CreateManifest(
            pack,
            configDescriptor,
            contentDescriptor,
            sourceMetadata.Source,
            sourceMetadata.Revision
        );
        if (!manifest.IsSuccess)
        {
            return ManifestOperationResult<PreparedArtifact>.Failure(
                manifest.Error ?? "Unable to create a valid LunaPack OCI manifest."
            );
        }

        var artifactManifest = manifest.Value;
        return ManifestOperationResult<PreparedArtifact>.Success(
            new PreparedArtifact(
                configContent,
                configDescriptor,
                contentDescriptor,
                artifactManifest.Content,
                artifactManifest.Descriptor
            )
        );
    }

    private async Task<ManifestOperationResult<bool>> PushArtifactAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        PublishTarget target,
        string archivePath,
        PreparedArtifact artifact,
        CancellationToken cancellationToken
    )
    {
        var configPush = await PushConfigAsync(
            projectDirectory,
            source,
            target.Context,
            target.Repository,
            artifact.ConfigContent,
            artifact.ConfigDescriptor,
            cancellationToken
        );
        if (configPush.Failure is { } configFailure)
        {
            return ManifestOperationResult<bool>.Failure(
                FormatFailure(
                    source,
                    target.Repository,
                    "Unable to upload pack metadata",
                    configFailure
                )
            );
        }

        var contentPush = await PushContentAsync(
            projectDirectory,
            source,
            target.Context,
            target.Repository,
            archivePath,
            artifact.ContentDescriptor,
            cancellationToken
        );
        if (contentPush.Failure is { } contentFailure)
        {
            return ManifestOperationResult<bool>.Failure(
                FormatFailure(
                    source,
                    target.Repository,
                    "Unable to upload pack content",
                    contentFailure
                )
            );
        }

        var manifestPush = await PushManifestAsync(
            projectDirectory,
            source,
            target.Context,
            target.Repository,
            target.Tag,
            artifact.ManifestContent,
            artifact.ManifestDescriptor,
            cancellationToken
        );
        return manifestPush.Failure is { } manifestFailure
            ? ManifestOperationResult<bool>.Failure(
                FormatFailure(
                    source,
                    target.Repository,
                    "Unable to publish pack manifest",
                    manifestFailure
                )
            )
            : ManifestOperationResult<bool>.Success(true);
    }

    private async Task<ManifestOperationResult<PackPublicationResult>> VerifyPublicationAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        PackManifest pack,
        PublishTarget target,
        OciRegistryDescriptor expectedManifest,
        CancellationToken cancellationToken
    )
    {
        var completion = await ResolveManifestAsync(
            projectDirectory,
            source,
            target.Context,
            target.Repository,
            target.Tag,
            cancellationToken
        );
        if (completion.Value is not { } publishedDescriptor)
        {
            return Failure<PackPublicationResult>(
                FormatFailure(
                    source,
                    target.Repository,
                    "Unable to verify publication completion",
                    completion.Failure
                )
            );
        }

        if (
            !string.Equals(
                publishedDescriptor.Digest,
                expectedManifest.Digest,
                StringComparison.Ordinal
            )
        )
        {
            return Failure<PackPublicationResult>(
                $"OCI source '{source.Name}' reported a different digest for '{target.Repository}:{target.Tag}' after publishing."
            );
        }

        var registryHost = new Uri(target.Context.RegistryUrl).Authority;
        return ManifestOperationResult<PackPublicationResult>.Success(
            new PackPublicationResult(
                pack.Id,
                pack.Version,
                source.Name,
                $"{registryHost}/{target.Repository}:{target.Tag}",
                publishedDescriptor.Digest
            )
        );
    }

    private async Task<ManifestOperationResult<bool>> EnsureTagIsAvailableAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        PublishContext context,
        string repository,
        string tag,
        CancellationToken cancellationToken
    )
    {
        var current = await ResolveManifestAsync(
            projectDirectory,
            source,
            context,
            repository,
            tag,
            cancellationToken
        );
        if (current.Value is not null)
        {
            return ManifestOperationResult<bool>.Failure(
                $"OCI release '{repository}:{tag}' already exists. Increment the pack version; published releases are immutable."
            );
        }

        return current.Failure?.Kind == OciRegistryFailureKind.NotFound
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(
                FormatFailure(
                    source,
                    repository,
                    $"Unable to check whether OCI release '{tag}' already exists",
                    current.Failure
                )
            );
    }

    private async Task<OciRegistryResult<OciRegistryUnit>> PushConfigAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        PublishContext context,
        string repository,
        byte[] content,
        OciRegistryDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new MemoryStream(content, writable: false);
        return await credentialCoordinator.ExecuteAsync(
            projectDirectory,
            source,
            credential =>
            {
                stream.Position = 0;
                return registryClient.PushBlobAsync(
                    context.Registry,
                    repository,
                    descriptor,
                    stream,
                    credential,
                    context.PartitionId,
                    cancellationToken
                );
            },
            cancellationToken
        );
    }

    private async Task<OciRegistryResult<OciRegistryUnit>> PushContentAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        PublishContext context,
        string repository,
        string archivePath,
        OciRegistryDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        await using var stream = fileSystem.File.OpenRead(archivePath);
        return await credentialCoordinator.ExecuteAsync(
            projectDirectory,
            source,
            credential =>
            {
                stream.Position = 0;
                return registryClient.PushBlobAsync(
                    context.Registry,
                    repository,
                    descriptor,
                    stream,
                    credential,
                    context.PartitionId,
                    cancellationToken
                );
            },
            cancellationToken
        );
    }

    private async Task<OciRegistryResult<OciRegistryUnit>> PushManifestAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        PublishContext context,
        string repository,
        string tag,
        byte[] content,
        OciRegistryDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new MemoryStream(content, writable: false);
        return await credentialCoordinator.ExecuteAsync(
            projectDirectory,
            source,
            credential =>
            {
                stream.Position = 0;
                return registryClient.PushManifestAsync(
                    context.Registry,
                    repository,
                    descriptor,
                    stream,
                    tag,
                    credential,
                    context.PartitionId,
                    cancellationToken
                );
            },
            cancellationToken
        );
    }

    private Task<OciRegistryResult<OciRegistryDescriptor>> ResolveManifestAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        PublishContext context,
        string repository,
        string reference,
        CancellationToken cancellationToken
    ) =>
        credentialCoordinator.ExecuteAsync(
            projectDirectory,
            source,
            credential =>
                registryClient.ResolveManifestAsync(
                    context.Registry,
                    repository,
                    reference,
                    credential,
                    context.PartitionId,
                    cancellationToken
                ),
            cancellationToken
        );

    private static ManifestOperationResult<PublishContext> CreateContext(
        IFileSystem fileSystem,
        string projectDirectory,
        ProjectConfiguration.OciSource source
    )
    {
        var location = SourceIdentityNormalizer.NormalizeOciLocation(source.Url, source.Path);
        if (!location.IsSuccess)
        {
            return ManifestOperationResult<PublishContext>.Failure(
                location.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        var credentialIdentity = OciSourceCredentialIdentity.Create(
            fileSystem,
            projectDirectory,
            source
        );
        return credentialIdentity.Value is { } partitionId
            ? ManifestOperationResult<PublishContext>.Success(
                new PublishContext(new Uri(location.Value.Url), location.Value.Url, partitionId)
            )
            : ManifestOperationResult<PublishContext>.Failure(
                credentialIdentity.Error ?? $"OCI source '{source.Name}' is invalid."
            );
    }

    private async Task<(string? Source, string? Revision)> ReadSourceMetadataAsync(
        string packDirectory,
        CancellationToken cancellationToken
    )
    {
        var sourceTask = gitProcessRunner.RunAsync(
            ["-C", packDirectory, "remote", "get-url", "origin"],
            _sourceMetadataTimeout,
            cancellationToken
        );
        var revisionTask = gitProcessRunner.RunAsync(
            ["-C", packDirectory, "rev-parse", "HEAD"],
            _sourceMetadataTimeout,
            cancellationToken
        );
        var results = await Task.WhenAll(sourceTask, revisionTask);
        cancellationToken.ThrowIfCancellationRequested();

        return (
            NormalizeSourceAnnotation(results[0].Value?.StandardOutput),
            NormalizeRevision(results[1].Value?.StandardOutput)
        );
    }

    private static string? NormalizeSourceAnnotation(string? value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > 4096
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var remote)
            || !string.Equals(remote.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(remote.Scheme, "ssh", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(remote.Host)
            || string.Equals(remote.AbsolutePath, "/", StringComparison.Ordinal)
        )
        {
            return null;
        }

        var sanitizedRemote = new UriBuilder(remote)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        var source = sanitizedRemote.Uri.GetLeftPart(UriPartial.Path);
        return source.Length <= 2048 ? source : null;
    }

    private static string? NormalizeRevision(string? value)
    {
        var revision = value?.Trim();
        return revision is { Length: 40 or 64 } && revision.All(char.IsAsciiHexDigit)
            ? revision.ToLowerInvariant()
            : null;
    }

    private static string FormatFailure(
        ProjectConfiguration.OciSource source,
        string repository,
        string operation,
        OciRegistryFailure? failure
    ) =>
        $"OCI source '{source.Name}' repository '{repository}': {operation}. {failure?.Message ?? "The registry operation failed."}";

    private static ManifestOperationResult<T> Failure<T>(string message) =>
        ManifestOperationResult<T>.Failure(message);

    private sealed record PublishContext(Uri Registry, string RegistryUrl, string PartitionId);

    private sealed record PublishTarget(PublishContext Context, string Repository, string Tag);

    private sealed record PreparedArtifact(
        byte[] ConfigContent,
        OciRegistryDescriptor ConfigDescriptor,
        OciRegistryDescriptor ContentDescriptor,
        byte[] ManifestContent,
        OciRegistryDescriptor ManifestDescriptor
    );
}
