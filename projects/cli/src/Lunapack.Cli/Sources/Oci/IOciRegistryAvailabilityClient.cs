namespace Lunapack.Cli.Sources.Oci;

internal interface IOciRegistryAvailabilityClient
{
    Task<OciRegistryResult<OciRegistryUnit>> ProbeAsync(
        Uri registry,
        string? repository,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    );
}
