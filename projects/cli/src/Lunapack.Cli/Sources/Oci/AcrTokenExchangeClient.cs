using Azure.Containers.ContainerRegistry;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class AcrTokenExchangeClient(ContainerRegistryClient client)
    : IAcrTokenExchangeClient
{
    public async Task<string> ExchangeAadAccessTokenForRefreshTokenAsync(
        string service,
        string tenant,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        var response = await client.ExchangeAadAccessTokenForAcrRefreshTokenAsync(
            service,
            tenant,
            string.Empty,
            accessToken,
            cancellationToken
        );
        return response.Value.RefreshToken;
    }

    public async Task<string> ExchangeRefreshTokenForAccessTokenAsync(
        string service,
        string scope,
        string refreshToken,
        CancellationToken cancellationToken
    )
    {
        var response = await client.ExchangeAcrRefreshTokenForAcrAccessTokenAsync(
            service,
            scope,
            refreshToken,
            cancellationToken
        );
        return response.Value.AccessToken;
    }
}
