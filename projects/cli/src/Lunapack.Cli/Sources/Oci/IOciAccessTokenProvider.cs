namespace Lunapack.Cli.Sources.Oci;

internal interface IOciAccessTokenProvider
{
    Task<string?> ResolveAccessTokenAsync(
        string hostname,
        string realm,
        string service,
        IReadOnlyList<string> scopes,
        bool forceRefresh,
        CancellationToken cancellationToken
    );
}
