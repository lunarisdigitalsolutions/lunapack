using Azure.Containers.ContainerRegistry;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class AcrTokenExchangeClientFactory : IAcrTokenExchangeClientFactory
{
    public IAcrTokenExchangeClient Create(Uri registryEndpoint) =>
        new AcrTokenExchangeClient(new ContainerRegistryClient(registryEndpoint));
}
