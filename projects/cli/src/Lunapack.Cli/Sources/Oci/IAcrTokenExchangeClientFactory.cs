namespace Lunapack.Cli.Sources.Oci;

internal interface IAcrTokenExchangeClientFactory
{
    IAcrTokenExchangeClient Create(Uri registryEndpoint);
}
