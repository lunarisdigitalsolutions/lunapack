using System.IO;

namespace Lunapack.Cli.Sources.Oci;

internal interface IOciRegistryClient
{
    Task<OciRegistryResult<IReadOnlyList<string>>> ListRepositoriesAsync(
        Uri registry,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    );

    Task<OciRegistryResult<IReadOnlyList<string>>> ListTagsAsync(
        Uri registry,
        string repository,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    );

    Task<OciRegistryResult<OciRegistryManifest>> FetchManifestAsync(
        Uri registry,
        string repository,
        string reference,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    );

    Task<OciRegistryResult<OciBlobTransfer>> FetchBlobAsync(
        Uri registry,
        string repository,
        OciRegistryDescriptor descriptor,
        Stream destination,
        long maximumBytes,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    );

    Task<OciRegistryResult<OciRegistryUnit>> PushBlobAsync(
        Uri registry,
        string repository,
        OciRegistryDescriptor descriptor,
        Stream content,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    );

    Task<OciRegistryResult<OciRegistryUnit>> PushManifestAsync(
        Uri registry,
        string repository,
        OciRegistryDescriptor descriptor,
        Stream content,
        string tag,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    );

    Task<OciRegistryResult<OciRegistryDescriptor>> ResolveManifestAsync(
        Uri registry,
        string repository,
        string reference,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    );
}
