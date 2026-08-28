using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Lunapack.Cli;

internal sealed class LinkResolver(
    IFileSystem fileSystem,
    LinkTargetMapper targetMapper,
    IReadOnlyList<ILinkSourceProvider> sourceProviders
)
{
    public async Task<ManifestOperationResult<LinkResolution>> ResolveAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        string linkName,
        ProjectConfiguration.Link link,
        ConfiguredSourceIdentity? lockedIdentity = null,
        ManagedFileTargetRemapping? targetRemapping = null,
        IReadOnlyDictionary<string, string>? retainedTargets = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(link);

        var binding = BindSource(linkName, configuration, link);
        if (binding.Value is not { } boundSource)
        {
            return ManifestOperationResult<LinkResolution>.Failure(
                binding.Error ?? $"Unable to bind link '{linkName}'."
            );
        }

        var (source, provider) = (boundSource.Source, boundSource.Provider);

        var listingResult = await provider.ListAsync(
            projectDirectory,
            source,
            link,
            lockedIdentity,
            cancellationToken
        );
        if (listingResult.Value is not { } listing)
        {
            return ManifestOperationResult<LinkResolution>.Failure(
                listingResult.Error ?? $"Unable to read link source '{link.Source}'."
            );
        }

        using var listingScope = listing;

        return await SelectAndSnapshotAsync(
            projectDirectory,
            linkName,
            link,
            source.Name,
            provider,
            listing,
            lockedIdentity,
            targetRemapping,
            ManagedFileTargetRemapping.FromConfiguration(configuration.Remap),
            retainedTargets,
            cancellationToken
        );
    }

    private async Task<ManifestOperationResult<LinkResolution>> SelectAndSnapshotAsync(
        string projectDirectory,
        string linkName,
        ProjectConfiguration.Link link,
        string sourceName,
        ILinkSourceProvider provider,
        LinkSourceListing listing,
        ConfiguredSourceIdentity? lockedIdentity,
        ManagedFileTargetRemapping? targetRemapping,
        ManagedFileTargetRemapping configuredRemapping,
        IReadOnlyDictionary<string, string>? retainedTargets,
        CancellationToken cancellationToken
    )
    {
        if (lockedIdentity is not null && listing.Identity != lockedIdentity)
        {
            return ManifestOperationResult<LinkResolution>.Failure(
                $"Source '{link.Source}' no longer provides the locked identity for link '{linkName}'."
            );
        }

        var selection = LinkSelectionService.Select(linkName, link, listing.Paths);
        if (selection.Value is not { } selectedPaths)
        {
            return ManifestOperationResult<LinkResolution>.Failure(
                selection.Error ?? $"Unable to select files for link '{linkName}'."
            );
        }

        var mapping = targetMapper.Map(
            projectDirectory,
            linkName,
            link,
            selectedPaths,
            targetRemapping,
            configuredRemapping,
            retainedTargets
        );
        if (mapping.Value is not { } mappings)
        {
            return ManifestOperationResult<LinkResolution>.Failure(
                mapping.Error ?? $"Unable to map targets for link '{linkName}'."
            );
        }

        return await SnapshotAsync(
            provider,
            listing,
            selectedPaths,
            mappings,
            linkName,
            link,
            sourceName,
            cancellationToken
        );
    }

    private ManifestOperationResult<BoundLinkSource> BindSource(
        string linkName,
        ProjectConfiguration configuration,
        ProjectConfiguration.Link link
    )
    {
        var source = configuration.Sources.Find(candidate =>
            string.Equals(candidate.Name, link.Source, StringComparison.Ordinal)
        );
        if (source is null)
        {
            return ManifestOperationResult<BoundLinkSource>.Failure(
                $"Link '{linkName}' references source '{link.Source}' which is not configured."
            );
        }

        var provider = sourceProviders.FirstOrDefault(candidate => candidate.CanProvide(source));
        return provider is null
            ? ManifestOperationResult<BoundLinkSource>.Failure(
                $"Link '{linkName}' references source '{link.Source}' with an unsupported type."
            )
            : ManifestOperationResult<BoundLinkSource>.Success(
                new BoundLinkSource(source, provider)
            );
    }

    private async Task<ManifestOperationResult<LinkResolution>> SnapshotAsync(
        ILinkSourceProvider provider,
        LinkSourceListing listing,
        IReadOnlyList<string> selectedPaths,
        IReadOnlyList<LinkFileMapping> mappings,
        string linkName,
        ProjectConfiguration.Link link,
        string sourceName,
        CancellationToken cancellationToken
    )
    {
        var workspace = LinkOperationWorkspace.Create(fileSystem);
        try
        {
            var materialization = await provider.MaterializeAsync(
                listing,
                selectedPaths,
                workspace,
                cancellationToken
            );
            if (materialization.Value is not { } snapshotPaths)
            {
                workspace.Dispose();
                return ManifestOperationResult<LinkResolution>.Failure(
                    materialization.Error ?? $"Unable to snapshot link '{linkName}'."
                );
            }

            var files = mappings
                .Select(fileMapping => CreateResolvedFile(fileMapping, snapshotPaths))
                .ToList();
            return ManifestOperationResult<LinkResolution>.Success(
                new LinkResolution(
                    workspace,
                    new ResolvedLinkSnapshot(
                        linkName,
                        LinkDefinitionHasher.ComputeSha256(linkName, link),
                        sourceName,
                        listing.Identity,
                        listing.GitSource,
                        files
                    )
                )
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            workspace.Dispose();
            return ManifestOperationResult<LinkResolution>.Failure(
                $"Unable to snapshot link '{linkName}': {exception.Message}"
            );
        }
    }

    private ResolvedLinkFile CreateResolvedFile(
        LinkFileMapping fileMapping,
        IReadOnlyDictionary<string, string> snapshotPaths
    )
    {
        var snapshotPath = snapshotPaths[fileMapping.SourcePath];
        return new ResolvedLinkFile(
            fileMapping.SourcePath,
            fileMapping.DeclaredTargetPath,
            fileMapping.TargetPath,
            Convert.ToHexString(SHA256.HashData(fileSystem.File.ReadAllBytes(snapshotPath))),
            snapshotPath
        );
    }
}
