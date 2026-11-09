using System.Text;
using Azure.Core;
using Azure.Identity;
using Lunapack.Cli.Application;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.UnitTests.Sources.Oci;

public sealed class AzureAcrAccessTokenProviderTests
{
    [Test]
    [Arguments("ghcr.io")]
    [Arguments("harbor.example.test")]
    [Arguments("registry.example.test")]
    public async Task Scenario_ResolveAccessToken_DoesNotUseAzureForNonAcrHosts(string hostname)
    {
        var credentialRequested = false;
        var exchangeFactory = new FakeAcrTokenExchangeClientFactory();
        var provider = new AzureAcrAccessTokenProvider(
            TestConsole.Create(),
            () =>
            {
                credentialRequested = true;
                throw new InvalidOperationException("Azure credentials should not be requested.");
            },
            exchangeFactory
        );

        var result = await provider.ResolveAccessTokenAsync(
            hostname,
            $"https://{hostname}/oauth2/token",
            hostname,
            ["repository:packs/backend:pull"],
            forceRefresh: false,
            CancellationToken.None
        );

        await Assert.That(result).IsNull();
        await Assert.That(credentialRequested).IsFalse();
        await Assert.That(exchangeFactory.CreateCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("azurecr.io", "https://containerregistry.azure.net/.default")]
    [Arguments("azurecr.cn", "https://containerregistry.azure.cn/.default")]
    [Arguments("azurecr.de", "https://containerregistry.azure.de/.default")]
    [Arguments("azurecr.us", "https://containerregistry.azure.us/.default")]
    public async Task Scenario_ResolveAccessToken_ExchangesAzureIdentityForRepositoryScope(
        string registrySuffix,
        string expectedScope
    )
    {
        var registryHost = $"lunapacks.{registrySuffix}";
        var credential = new FakeTokenCredential(CreateAccessToken());
        var exchangeClient = new FakeAcrTokenExchangeClient();
        var exchangeFactory = new FakeAcrTokenExchangeClientFactory(exchangeClient);
        var provider = new AzureAcrAccessTokenProvider(
            TestConsole.Create(),
            () => credential,
            exchangeFactory
        );

        var result = await provider.ResolveAccessTokenAsync(
            registryHost,
            $"https://{registryHost}/oauth2/token",
            registryHost,
            ["repository:packs/backend:pull", "repository:packs/backend:push"],
            forceRefresh: false,
            CancellationToken.None
        );

        await Assert.That(result).IsEqualTo("acr-access-token");
        await Assert.That(credential.RequestedScopes).IsEquivalentTo([expectedScope]);
        await Assert.That(exchangeFactory.LastEndpoint?.Host).IsEqualTo(registryHost);
        await Assert.That(exchangeClient.Service).IsEqualTo(registryHost);
        await Assert.That(exchangeClient.Tenant).IsEqualTo("11111111-2222-3333-4444-555555555555");
        await Assert.That(exchangeClient.AadAccessToken).IsEqualTo(CreateAccessToken());
        await Assert
            .That(exchangeClient.Scope)
            .IsEqualTo("repository:packs/backend:pull repository:packs/backend:push");
    }

    [Test]
    public async Task Scenario_ResolveAccessToken_RejectsUntrustedTokenRealm()
    {
        var credentialRequested = false;
        var exchangeFactory = new FakeAcrTokenExchangeClientFactory();
        var provider = new AzureAcrAccessTokenProvider(
            TestConsole.Create(),
            () =>
            {
                credentialRequested = true;
                return new FakeTokenCredential(CreateAccessToken());
            },
            exchangeFactory
        );

        var result = await provider.ResolveAccessTokenAsync(
            "lunapacks.azurecr.io",
            "https://attacker.example/oauth2/token",
            "lunapacks.azurecr.io",
            ["repository:packs/backend:pull"],
            forceRefresh: false,
            CancellationToken.None
        );

        await Assert.That(result).IsNull();
        await Assert.That(credentialRequested).IsFalse();
        await Assert.That(exchangeFactory.CreateCount).IsEqualTo(0);
    }

    [Test]
    public async Task Scenario_ResolveAccessToken_ReturnsNullWhenAzureIdentityIsUnavailable()
    {
        var provider = new AzureAcrAccessTokenProvider(
            TestConsole.Create(),
            static () =>
                new FakeTokenCredential(
                    CreateAccessToken(),
                    new AuthenticationFailedException("credential failure")
                ),
            new FakeAcrTokenExchangeClientFactory()
        );

        var result = await provider.ResolveAccessTokenAsync(
            "lunapacks.azurecr.io",
            "https://lunapacks.azurecr.io/oauth2/token",
            "lunapacks.azurecr.io",
            ["repository:packs/backend:pull"],
            forceRefresh: false,
            CancellationToken.None
        );

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Scenario_ResolveAccessToken_FallsBackWhenAzureIdentityTimesOut()
    {
        var exchangeFactory = new FakeAcrTokenExchangeClientFactory();
        var provider = new AzureAcrAccessTokenProvider(
            TestConsole.Create(),
            static () => new BlockingTokenCredential(),
            exchangeFactory,
            TimeSpan.FromMilliseconds(20)
        );

        var result = await provider.ResolveAccessTokenAsync(
            "lunapacks.azurecr.io",
            "https://lunapacks.azurecr.io/oauth2/token",
            "lunapacks.azurecr.io",
            ["repository:packs/backend:pull"],
            forceRefresh: false,
            CancellationToken.None
        );

        await Assert.That(result).IsNull();
        await Assert.That(exchangeFactory.CreateCount).IsEqualTo(0);
    }

    private static string CreateAccessToken()
    {
        var payload = Convert
            .ToBase64String(
                Encoding.UTF8.GetBytes("""{"tid":"11111111-2222-3333-4444-555555555555"}""")
            )
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"header.{payload}.signature";
    }

    private sealed class FakeTokenCredential(string token, Exception? failure = null)
        : TokenCredential
    {
        public IReadOnlyList<string> RequestedScopes { get; private set; } = [];

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            RequestedScopes = requestContext.Scopes;
            return failure is not null
                ? ValueTask.FromException<AccessToken>(failure)
                : ValueTask.FromResult(new AccessToken(token, DateTimeOffset.UtcNow.AddMinutes(5)));
        }
    }

    private sealed class BlockingTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override async ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return default;
        }
    }

    private sealed class FakeAcrTokenExchangeClientFactory(
        FakeAcrTokenExchangeClient? client = null
    ) : IAcrTokenExchangeClientFactory
    {
        public Uri? LastEndpoint { get; private set; }

        public int CreateCount { get; private set; }

        public IAcrTokenExchangeClient Create(Uri registryEndpoint)
        {
            LastEndpoint = registryEndpoint;
            CreateCount++;
            return client ?? new FakeAcrTokenExchangeClient();
        }
    }

    private sealed class FakeAcrTokenExchangeClient : IAcrTokenExchangeClient
    {
        public string? Service { get; private set; }

        public string? Tenant { get; private set; }

        public string? AadAccessToken { get; private set; }

        public string? Scope { get; private set; }

        public Task<string> ExchangeAadAccessTokenForRefreshTokenAsync(
            string service,
            string tenant,
            string accessToken,
            CancellationToken cancellationToken
        )
        {
            Service = service;
            Tenant = tenant;
            AadAccessToken = accessToken;
            return Task.FromResult("acr-refresh-token");
        }

        public Task<string> ExchangeRefreshTokenForAccessTokenAsync(
            string service,
            string scope,
            string refreshToken,
            CancellationToken cancellationToken
        )
        {
            Service = service;
            Scope = scope;
            return Task.FromResult("acr-access-token");
        }
    }
}
