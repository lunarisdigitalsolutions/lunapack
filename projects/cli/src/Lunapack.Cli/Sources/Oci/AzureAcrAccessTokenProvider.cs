using System.Collections.Concurrent;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using Lunapack.Cli.Application;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class AzureAcrAccessTokenProvider : IOciAccessTokenProvider
{
    private const string AzureContainerRegistryScope =
        "https://containerregistry.azure.net/.default";
    private static readonly TimeSpan _identityTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] _registrySuffixes =
    [
        ".azurecr.io",
        ".azurecr.cn",
        ".azurecr.de",
        ".azurecr.us",
    ];

    private readonly CliConsole _console;
    private readonly Lazy<TokenCredential> _credential;
    private readonly IAcrTokenExchangeClientFactory _clientFactory;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<string, Lazy<IAcrTokenExchangeClient>> _clients = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ConcurrentDictionary<string, byte> _warnedHosts = new(
        StringComparer.OrdinalIgnoreCase
    );

    public AzureAcrAccessTokenProvider(CliConsole console)
        : this(
            console,
            static () => new DefaultAzureCredential(),
            new AcrTokenExchangeClientFactory(),
            _identityTimeout
        ) { }

    internal AzureAcrAccessTokenProvider(
        CliConsole console,
        Func<TokenCredential> credentialFactory,
        IAcrTokenExchangeClientFactory clientFactory,
        TimeSpan? timeout = null
    )
    {
        _console = console;
        _credential = new Lazy<TokenCredential>(
            credentialFactory,
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        _clientFactory = clientFactory;
        _timeout = timeout ?? _identityTimeout;
    }

    public async Task<string?> ResolveAccessTokenAsync(
        string hostname,
        string realm,
        string service,
        IReadOnlyList<string> scopes,
        bool forceRefresh,
        CancellationToken cancellationToken
    )
    {
        if (
            !TryGetRegistryEndpoint(hostname, realm, service, out var registryEndpoint)
            || !AreValidScopes(scopes)
        )
        {
            return null;
        }

        using var identityTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        identityTimeout.CancelAfter(_timeout);
        try
        {
            var aadToken = await _credential.Value.GetTokenAsync(
                new TokenRequestContext([GetAzureContainerRegistryScope(registryEndpoint.Host)]),
                identityTimeout.Token
            );
            var tenantId =
                ReadTenantId(aadToken.Token)
                ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID")?.Trim();
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                WarnUnavailable(registryEndpoint.Host);
                return null;
            }

            var client = GetExchangeClient(registryEndpoint);
            var refreshToken = await client.ExchangeAadAccessTokenForRefreshTokenAsync(
                registryEndpoint.Host,
                tenantId,
                aadToken.Token,
                identityTimeout.Token
            );
            return await client.ExchangeRefreshTokenForAccessTokenAsync(
                registryEndpoint.Host,
                string.Join(" ", scopes),
                refreshToken,
                identityTimeout.Token
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            WarnUnavailable(registryEndpoint.Host);
            return null;
        }
        catch (CredentialUnavailableException)
        {
            WarnUnavailable(registryEndpoint.Host);
            return null;
        }
        catch (AuthenticationFailedException)
        {
            WarnUnavailable(registryEndpoint.Host);
            return null;
        }
        catch (RequestFailedException)
        {
            WarnUnavailable(registryEndpoint.Host);
            return null;
        }
    }

    private IAcrTokenExchangeClient GetExchangeClient(Uri registryEndpoint) =>
        _clients
            .GetOrAdd(
                registryEndpoint.Host,
                host => new Lazy<IAcrTokenExchangeClient>(
                    () => _clientFactory.Create(registryEndpoint),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            )
            .Value;

    private void WarnUnavailable(string registryHost)
    {
        if (_warnedHosts.TryAdd(registryHost, 0))
        {
            _console.Warning(
                $"Azure identity could not obtain an ACR token for '{registryHost}'. Configure Azure Identity (for example, run 'az login' or configure workload federation), or supply source-bound registry credentials; trying other credentials."
            );
        }
    }

    private static bool AreValidScopes(IReadOnlyList<string> scopes) =>
        scopes.Count is > 0 and <= 16
        && scopes.All(scope =>
            !string.IsNullOrWhiteSpace(scope) && scope.Length <= 2048 && !scope.Any(char.IsControl)
        );

    private static bool TryGetRegistryEndpoint(
        string hostname,
        string realm,
        string service,
        out Uri registryEndpoint
    )
    {
        registryEndpoint = null!;
        if (
            string.IsNullOrWhiteSpace(hostname)
            || !Uri.TryCreate($"https://{hostname.Trim()}", UriKind.Absolute, out var endpoint)
            || endpoint.UserInfo.Length != 0
            || !string.Equals(endpoint.AbsolutePath, "/", StringComparison.Ordinal)
            || !string.Equals(endpoint.Query, string.Empty, StringComparison.Ordinal)
            || !string.Equals(endpoint.Fragment, string.Empty, StringComparison.Ordinal)
            || endpoint.Port != 443
            || !IsAcrHost(endpoint.Host)
            || !string.Equals(service, endpoint.Host, StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(realm, UriKind.Absolute, out var realmUri)
            || !string.Equals(
                realmUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase
            )
            || realmUri.UserInfo.Length != 0
            || !string.Equals(realmUri.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase)
            || realmUri.Port != 443
            || !string.Equals(realmUri.AbsolutePath, "/oauth2/token", StringComparison.Ordinal)
            || !string.Equals(realmUri.Query, string.Empty, StringComparison.Ordinal)
            || !string.Equals(realmUri.Fragment, string.Empty, StringComparison.Ordinal)
        )
        {
            return false;
        }

        registryEndpoint = new UriBuilder(Uri.UriSchemeHttps, endpoint.Host).Uri;
        return true;
    }

    private static bool IsAcrHost(string hostname) =>
        _registrySuffixes.Any(suffix =>
            hostname.Length > suffix.Length
            && hostname.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
        );

    private static string GetAzureContainerRegistryScope(string registryHost)
    {
        if (registryHost.EndsWith(".azurecr.cn", StringComparison.OrdinalIgnoreCase))
        {
            return "https://containerregistry.azure.cn/.default";
        }

        if (registryHost.EndsWith(".azurecr.de", StringComparison.OrdinalIgnoreCase))
        {
            return "https://containerregistry.azure.de/.default";
        }

        if (registryHost.EndsWith(".azurecr.us", StringComparison.OrdinalIgnoreCase))
        {
            return "https://containerregistry.azure.us/.default";
        }

        return AzureContainerRegistryScope;
    }

    private static string? ReadTenantId(string accessToken)
    {
        var segments = accessToken.Split('.');
        if (segments.Length != 3)
        {
            return null;
        }

        var payload = segments[1].Replace('-', '+').Replace('_', '/');
        payload += new string('=', (4 - payload.Length % 4) % 4);
        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("tid", out var tenantId)
                || tenantId.ValueKind != JsonValueKind.String
                || !Guid.TryParse(tenantId.GetString(), out _)
            )
            {
                return null;
            }

            return tenantId.GetString();
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
