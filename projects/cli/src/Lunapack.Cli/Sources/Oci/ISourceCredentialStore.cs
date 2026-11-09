using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Oci;

internal interface ISourceCredentialStore
{
    ManifestOperationResult<OciRegistryCredential?> Get(string key);

    ManifestOperationResult<bool> Remove(string key);

    ManifestOperationResult<bool> Save(string key, OciRegistryCredential credential);
}
