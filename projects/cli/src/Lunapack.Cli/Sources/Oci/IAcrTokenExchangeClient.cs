namespace Lunapack.Cli.Sources.Oci;

internal interface IAcrTokenExchangeClient
{
    Task<string> ExchangeAadAccessTokenForRefreshTokenAsync(
        string service,
        string tenant,
        string accessToken,
        CancellationToken cancellationToken
    );

    Task<string> ExchangeRefreshTokenForAccessTokenAsync(
        string service,
        string scope,
        string refreshToken,
        CancellationToken cancellationToken
    );
}
