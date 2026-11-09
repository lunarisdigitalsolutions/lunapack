using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.UnitTests.Sources.Oci;

public sealed class OciDistributionRegistryClientTests
{
    private static readonly Uri _registry = new("https://registry.example.test");

    [Test]
    [Arguments(HttpStatusCode.Unauthorized)]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.NotFound)]
    [Arguments(HttpStatusCode.Conflict)]
    public async Task Scenario_RegistryStatus_MapsToDomainFailure(HttpStatusCode statusCode)
    {
        var client = CreateClient((_, _) => Task.FromResult(CreateResponse(statusCode)));

        var result = await client.ResolveManifestAsync(
            _registry,
            "team/backend",
            "1.0.0",
            null,
            "partition",
            CancellationToken.None
        );

        var expectedFailure = statusCode switch
        {
            HttpStatusCode.Unauthorized => OciRegistryFailureKind.AuthenticationRequired,
            HttpStatusCode.Forbidden => OciRegistryFailureKind.AuthorizationDenied,
            HttpStatusCode.NotFound => OciRegistryFailureKind.NotFound,
            HttpStatusCode.Conflict => OciRegistryFailureKind.Conflict,
            _ => throw new ArgumentOutOfRangeException(nameof(statusCode)),
        };
        await Assert.That(result.Failure?.Kind).IsEqualTo(expectedFailure);
    }

    [Test]
    public async Task Scenario_RegistryCatalogPagination_ReturnsEveryRepository()
    {
        var requestUris = new List<Uri?>();
        var client = CreateClient(
            (request, _) =>
            {
                requestUris.Add(request.RequestUri);
                var response = CreateJsonResponse(
                    HttpStatusCode.OK,
                    requestUris.Count == 1
                        ? "{\"repositories\":[\"team/backend\"]}"
                        : "{\"repositories\":[\"team/security\"]}"
                );
                if (requestUris.Count == 1)
                {
                    response.Headers.TryAddWithoutValidation(
                        "Link",
                        "</v2/_catalog?n=100&last=team%2Fbackend>; rel=\"next\""
                    );
                }

                return Task.FromResult(response);
            }
        );

        var result = await client.ListRepositoriesAsync(
            _registry,
            null,
            "partition",
            CancellationToken.None
        );
        var repositories = result.Value?.ToArray() ?? [];

        await Assert
            .That(result.IsSuccess)
            .IsTrue()
            .Because(result.Failure?.Message ?? string.Empty);
        await Assert.That(repositories.SequenceEqual(["team/backend", "team/security"])).IsTrue();
        await Assert.That(requestUris.Count).IsEqualTo(2);
        await Assert.That(requestUris[0]?.Query).IsEqualTo("?n=100");
    }

    [Test]
    public async Task Scenario_RegistryCatalogMissingRepositoryList_IsInvalidResponse()
    {
        var client = CreateClient(
            (_, _) => Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, "{}"))
        );

        var result = await client.ListRepositoriesAsync(
            _registry,
            null,
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.InvalidResponse);
    }

    [Test]
    public async Task Scenario_RegistryCatalogPagination_RejectsCrossOriginLink()
    {
        var requestCount = 0;
        var client = CreateClient(
            (_, _) =>
            {
                requestCount++;
                var response = CreateJsonResponse(HttpStatusCode.OK, "{\"repositories\":[]}");
                response.Headers.TryAddWithoutValidation(
                    "Link",
                    "<https://attacker.example.test/v2/_catalog?n=100>; rel=\"next\""
                );
                return Task.FromResult(response);
            }
        );

        var result = await client.ListRepositoriesAsync(
            _registry,
            null,
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.InvalidResponse);
        await Assert.That(requestCount).IsEqualTo(1);
    }

    [Test]
    public async Task Scenario_RegistryTagList_ParsesJsonFromRepositoryEndpoint()
    {
        Uri? requestUri = null;
        var client = CreateClient(
            (request, _) =>
            {
                requestUri = request.RequestUri;
                return Task.FromResult(
                    CreateJsonResponse(
                        HttpStatusCode.OK,
                        "{\"name\":\"team/backend\",\"tags\":[\"1.0.0\",\"1.1.0\"]}"
                    )
                );
            }
        );

        var result = await client.ListTagsAsync(
            _registry,
            "team/backend",
            null,
            "partition",
            CancellationToken.None
        );
        var tags = result.Value?.ToArray() ?? [];

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(tags.SequenceEqual(["1.0.0", "1.1.0"])).IsTrue();
        await Assert.That(requestUri?.AbsolutePath).IsEqualTo("/v2/team/backend/tags/list");
        await Assert.That(requestUri?.Query).IsEqualTo("?n=100");
    }

    [Test]
    public async Task Scenario_RegistryDnsFailure_MapsToDnsFailure()
    {
        var client = CreateClient(
            (_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new HttpRequestException(
                        HttpRequestError.NameResolutionError,
                        "DNS lookup failed."
                    )
                )
        );

        var result = await client.ResolveManifestAsync(
            _registry,
            "team/backend",
            "1.0.0",
            null,
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.Dns);
    }

    [Test]
    public async Task Scenario_RegistryTlsFailure_MapsToTlsFailure()
    {
        var client = CreateClient(
            (_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new HttpRequestException(
                        HttpRequestError.SecureConnectionError,
                        "TLS verification failed."
                    )
                )
        );

        var result = await client.ResolveManifestAsync(
            _registry,
            "team/backend",
            "1.0.0",
            null,
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.Tls);
    }

    [Test]
    public async Task Scenario_RegistryOutage_MapsToUnavailable()
    {
        var client = CreateClient(
            (_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new HttpRequestException(
                        HttpRequestError.ConnectionError,
                        "Connection refused."
                    )
                )
        );

        var result = await client.ResolveManifestAsync(
            _registry,
            "team/backend",
            "1.0.0",
            null,
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.Unavailable);
    }

    [Test]
    public async Task Scenario_RegistryRequestTimeout_MapsToTimeout()
    {
        var client = CreateClient(
            (_, cancellationToken) =>
                new TaskCompletionSource<HttpResponseMessage>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                ).Task.WaitAsync(cancellationToken),
            TimeSpan.FromMilliseconds(100)
        );

        var result = await client.ResolveManifestAsync(
            _registry,
            "team/backend",
            "1.0.0",
            null,
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.Timeout);
    }

    [Test]
    public async Task Scenario_RegistryRequestCancellation_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var client = CreateClient(
            (_, cancellationToken) =>
                new TaskCompletionSource<HttpResponseMessage>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                ).Task.WaitAsync(cancellationToken),
            TimeSpan.FromSeconds(2)
        );
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        var canceled = false;

        try
        {
            await client.ResolveManifestAsync(
                _registry,
                "team/backend",
                "1.0.0",
                null,
                "partition",
                cancellation.Token
            );
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        await Assert.That(canceled).IsTrue();
    }

    [Test]
    public async Task Scenario_RegistryRejectsCrossHostChallenge_WithoutForwardingCredential()
    {
        var requests = new List<Uri?>();
        var client = CreateClient(
            (request, _) =>
            {
                requests.Add(request.RequestUri);
                var response = CreateResponse(HttpStatusCode.Unauthorized);
                response.Headers.TryAddWithoutValidation(
                    "WWW-Authenticate",
                    "Bearer realm=\"https://attacker.example.test/token\",service=\"registry.example.test\""
                );
                return Task.FromResult(response);
            }
        );

        var result = await client.ResolveManifestAsync(
            _registry,
            "team/backend",
            "1.0.0",
            new OciRegistryCredential("user", "secret-sentinel"),
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.InvalidResponse);
        await Assert
            .That(
                requests.Any(request =>
                    request is not null
                    && string.Equals(
                        request.Host,
                        "attacker.example.test",
                        StringComparison.Ordinal
                    )
                )
            )
            .IsFalse();
        await Assert.That(result.Failure?.Message).DoesNotContain("secret-sentinel");
    }

    [Test]
    public async Task Scenario_RegistryCredentialChallengeOnNonDefaultPort_UsesCredential()
    {
        var registry = new Uri("https://registry.example.test:5443");
        var basicCredentialWasUsed = false;
        var firstRequest = true;
        var client = CreateClient(
            (request, _) =>
            {
                if (
                    request.Headers.Authorization?.Scheme is { } scheme
                    && string.Equals(scheme, "Basic", StringComparison.Ordinal)
                )
                {
                    basicCredentialWasUsed = true;
                }

                if (firstRequest)
                {
                    firstRequest = false;
                    var response = CreateResponse(HttpStatusCode.Unauthorized);
                    response.Headers.TryAddWithoutValidation(
                        "WWW-Authenticate",
                        "Basic realm=\"https://registry.example.test:5443/auth\""
                    );
                    return Task.FromResult(response);
                }

                return Task.FromResult(CreateResponse(HttpStatusCode.NotFound));
            }
        );

        var result = await client.ResolveManifestAsync(
            registry,
            "team/backend",
            "1.0.0",
            new OciRegistryCredential("user", "secret-sentinel"),
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.NotFound);
        await Assert.That(basicCredentialWasUsed).IsTrue();
    }

    [Test]
    public async Task Scenario_BearerChallenge_ExchangesSourceCredentialForRepositoryScope()
    {
        var requests = new List<(Uri? Uri, AuthenticationHeaderValue? Authorization)>();
        var client = CreateClient(
            (request, _) =>
            {
                requests.Add((request.RequestUri, request.Headers.Authorization));
                if (
                    string.Equals(
                        request.RequestUri?.AbsolutePath,
                        "/token",
                        StringComparison.Ordinal
                    )
                )
                {
                    return Task.FromResult(
                        CreateJsonResponse(HttpStatusCode.OK, "{\"token\":\"access-token\"}")
                    );
                }

                if (
                    string.Equals(
                        request.Headers.Authorization?.Scheme,
                        "Bearer",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return Task.FromResult(CreateResponse(HttpStatusCode.NotFound));
                }

                var response = CreateResponse(HttpStatusCode.Unauthorized);
                response.Headers.TryAddWithoutValidation(
                    "WWW-Authenticate",
                    "Bearer realm=\"https://registry.example.test/token\",service=\"registry.example.test\",scope=\"repository:team/backend:pull\""
                );
                return Task.FromResult(response);
            }
        );

        var result = await client.ResolveManifestAsync(
            _registry,
            "team/backend",
            "1.0.0",
            new OciRegistryCredential("registry-user", "secret-sentinel"),
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.NotFound);
        await Assert.That(requests.Count).IsEqualTo(3);
        await Assert.That(requests[0].Authorization).IsNull();
        await Assert.That(requests[1].Uri?.AbsolutePath).IsEqualTo("/token");
        await Assert.That(requests[1].Authorization?.Scheme).IsEqualTo("Basic");
        await Assert
            .That(requests[1].Uri?.Query)
            .Contains("scope=repository%3Ateam%2Fbackend%3Apull");
        await Assert.That(requests[2].Authorization?.ToString()).IsEqualTo("Bearer access-token");
    }

    [Test]
    public async Task Scenario_BearerChallenge_RejectsExpandedRepositoryScope()
    {
        var tokenRequestCount = 0;
        var client = CreateClient(
            (request, _) =>
            {
                if (
                    string.Equals(
                        request.RequestUri?.AbsolutePath,
                        "/token",
                        StringComparison.Ordinal
                    )
                )
                {
                    tokenRequestCount++;
                    return Task.FromResult(
                        CreateJsonResponse(HttpStatusCode.OK, "{\"token\":\"access-token\"}")
                    );
                }

                var response = CreateResponse(HttpStatusCode.Unauthorized);
                response.Headers.TryAddWithoutValidation(
                    "WWW-Authenticate",
                    "Bearer realm=\"https://registry.example.test/token\",service=\"registry.example.test\",scope=\"repository:team/backend:push\""
                );
                return Task.FromResult(response);
            }
        );

        var result = await client.ResolveManifestAsync(
            _registry,
            "team/backend",
            "1.0.0",
            new OciRegistryCredential("registry-user", "secret-sentinel"),
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.InvalidResponse);
        await Assert.That(tokenRequestCount).IsEqualTo(0);
        await Assert.That(result.Failure?.Message).DoesNotContain("secret-sentinel");
    }

    [Test]
    public async Task Scenario_BearerChallenge_RefreshesRejectedTokenOnlyOnce()
    {
        var tokenRequestCount = 0;
        var bearerTokens = new List<string?>();
        var client = CreateClient(
            (request, _) =>
            {
                if (
                    string.Equals(
                        request.RequestUri?.AbsolutePath,
                        "/token",
                        StringComparison.Ordinal
                    )
                )
                {
                    tokenRequestCount++;
                    return Task.FromResult(
                        CreateJsonResponse(
                            HttpStatusCode.OK,
                            $"{{\"token\":\"access-token-{tokenRequestCount}\"}}"
                        )
                    );
                }

                var token = request.Headers.Authorization?.Parameter;
                bearerTokens.Add(token);
                if (string.Equals(token, "access-token-2", StringComparison.Ordinal))
                {
                    return Task.FromResult(CreateResponse(HttpStatusCode.NotFound));
                }

                var response = CreateResponse(HttpStatusCode.Unauthorized);
                response.Headers.TryAddWithoutValidation(
                    "WWW-Authenticate",
                    "Bearer realm=\"https://registry.example.test/token\",service=\"registry.example.test\",scope=\"repository:team/backend:pull\""
                );
                return Task.FromResult(response);
            }
        );

        var result = await client.ResolveManifestAsync(
            _registry,
            "team/backend",
            "1.0.0",
            null,
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.NotFound);
        await Assert.That(tokenRequestCount).IsEqualTo(2);
        await Assert
            .That(
                bearerTokens.SequenceEqual(
                    [null, "access-token-1", "access-token-2"],
                    StringComparer.Ordinal
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task Scenario_BlobPush_UsesValidatedUploadLocationAndDigest()
    {
        var content = Encoding.UTF8.GetBytes("registry-blob-content");
        var descriptor = OciPackArtifact.CreateDescriptor(OciPackMediaTypes.ContentZip, content);
        var methods = new List<HttpMethod>();
        Uri? uploadStartUri = null;
        Uri? uploadUri = null;
        string? uploadedContent = null;
        var client = CreateClient(
            async (request, cancellationToken) =>
            {
                methods.Add(request.Method);
                if (request.Method == HttpMethod.Head)
                {
                    return CreateResponse(HttpStatusCode.NotFound);
                }

                if (request.Method == HttpMethod.Post)
                {
                    uploadStartUri = request.RequestUri;
                    var response = CreateResponse(HttpStatusCode.Accepted);
                    response.Headers.Location = new Uri(
                        "/v2/team/backend/blobs/uploads/session",
                        UriKind.Relative
                    );
                    return response;
                }

                uploadUri = request.RequestUri;
                uploadedContent = await request.Content!.ReadAsStringAsync(cancellationToken);
                var uploadResponse = CreateResponse(HttpStatusCode.Created);
                uploadResponse.Headers.TryAddWithoutValidation(
                    "Docker-Content-Digest",
                    descriptor.Digest
                );
                return uploadResponse;
            }
        );
        using var stream = new MemoryStream(content);
        stream.Position = 3;

        var result = await client.PushBlobAsync(
            _registry,
            "team/backend",
            descriptor,
            stream,
            null,
            "partition",
            CancellationToken.None
        );

        await Assert
            .That(result.IsSuccess)
            .IsTrue()
            .Because(result.Failure?.Message ?? string.Empty);
        await Assert
            .That(methods.SequenceEqual([HttpMethod.Head, HttpMethod.Post, HttpMethod.Put]))
            .IsTrue();
        await Assert
            .That(uploadStartUri?.AbsolutePath)
            .IsEqualTo("/v2/team/backend/blobs/uploads/");
        await Assert
            .That(uploadUri?.AbsolutePath)
            .IsEqualTo("/v2/team/backend/blobs/uploads/session");
        await Assert
            .That(uploadUri?.Query)
            .IsEqualTo($"?digest={Uri.EscapeDataString(descriptor.Digest)}");
        await Assert.That(uploadedContent).IsEqualTo(Encoding.UTF8.GetString(content));
    }

    [Test]
    public async Task Scenario_BlobPush_RejectsCrossOriginUploadLocation()
    {
        var content = Encoding.UTF8.GetBytes("registry-blob-content");
        var descriptor = OciPackArtifact.CreateDescriptor(OciPackMediaTypes.ContentZip, content);
        var putRequestCount = 0;
        var client = CreateClient(
            (request, _) =>
            {
                if (request.Method == HttpMethod.Head)
                {
                    return Task.FromResult(CreateResponse(HttpStatusCode.NotFound));
                }

                if (request.Method == HttpMethod.Post)
                {
                    var response = CreateResponse(HttpStatusCode.Accepted);
                    response.Headers.Location = new Uri(
                        "https://attacker.example.test/v2/team/backend/blobs/uploads/session"
                    );
                    return Task.FromResult(response);
                }

                putRequestCount++;
                return Task.FromResult(CreateResponse(HttpStatusCode.Created));
            }
        );
        using var stream = new MemoryStream(content);

        var result = await client.PushBlobAsync(
            _registry,
            "team/backend",
            descriptor,
            stream,
            new OciRegistryCredential("registry-user", "secret-sentinel"),
            "partition",
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.InvalidResponse);
        await Assert.That(putRequestCount).IsEqualTo(0);
        await Assert.That(result.Failure?.Message).DoesNotContain("secret-sentinel");
    }

    [Test]
    public async Task Scenario_ManifestPush_UsesAuthenticatedRegistryClientAndPreservesContent()
    {
        var content = Encoding.UTF8.GetBytes("{\"schemaVersion\":2}");
        var descriptor = OciPackArtifact.CreateDescriptor(OciPackMediaTypes.Manifest, content);
        HttpMethod? requestMethod = null;
        Uri? requestUri = null;
        string? requestMediaType = null;
        string? requestContent = null;
        var client = CreateClient(
            async (request, cancellationToken) =>
            {
                requestMethod = request.Method;
                requestUri = request.RequestUri;
                requestMediaType = request.Content?.Headers.ContentType?.MediaType;
                requestContent = await request.Content!.ReadAsStringAsync(cancellationToken);
                var response = CreateResponse(HttpStatusCode.Created);
                response.Headers.TryAddWithoutValidation(
                    "Docker-Content-Digest",
                    descriptor.Digest
                );
                return response;
            }
        );

        using var stream = new MemoryStream(content);
        var result = await client.PushManifestAsync(
            _registry,
            "team/backend",
            descriptor,
            stream,
            "1.0.0",
            null,
            "partition",
            CancellationToken.None
        );

        await Assert
            .That(result.IsSuccess)
            .IsTrue()
            .Because(result.Failure?.Message ?? string.Empty);
        await Assert.That(requestMethod).IsEqualTo(HttpMethod.Put);
        await Assert
            .That(requestUri)
            .IsEqualTo(new Uri("https://registry.example.test/v2/team/backend/manifests/1.0.0"));
        await Assert.That(requestMediaType).IsEqualTo(OciPackMediaTypes.Manifest);
        await Assert.That(requestContent).IsEqualTo(Encoding.UTF8.GetString(content));
    }

    [Test]
    public async Task Scenario_SocketsHttpHandlerRedirectAcrossHost_DoesNotForwardAuthorization()
    {
        using var portAllocator = new TcpListener(IPAddress.Loopback, 0);
        portAllocator.Start();
        var port = ((IPEndPoint)portAllocator.LocalEndpoint).Port;
        portAllocator.Stop();

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var observedRequests = Task.Run(async () =>
        {
            var initial = await listener.GetContextAsync();
            var initialAuthorization = initial.Request.Headers["Authorization"];
            initial.Response.StatusCode = (int)HttpStatusCode.Redirect;
            initial.Response.RedirectLocation = $"http://localhost:{port}/redirected";
            initial.Response.Close();

            var redirected = await listener.GetContextAsync();
            var redirectedAuthorization = redirected.Request.Headers["Authorization"];
            redirected.Response.StatusCode = (int)HttpStatusCode.OK;
            redirected.Response.Close();
            return (initialAuthorization, redirectedAuthorization);
        });
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = true };
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{port}/registry"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("user:secret-sentinel"))
        );

        using var response = await httpClient.SendAsync(request);
        var (initialAuthorization, redirectedAuthorization) = await observedRequests.WaitAsync(
            TimeSpan.FromSeconds(5)
        );

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(initialAuthorization).StartsWith("Basic ");
        await Assert.That(redirectedAuthorization).IsNull();
    }

    private static OciDistributionRegistryClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        TimeSpan? timeout = null
    ) => new(timeout, httpMessageHandlerFactory: () => new CallbackHttpMessageHandler(send));

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                "{\"errors\":[{\"code\":\"UNAUTHORIZED\",\"message\":\"registry request failed\"}]}"
            ),
        };
        response.Content.Headers.ContentType = new("application/json");
        return response;
    }

    private static HttpResponseMessage CreateJsonResponse(
        HttpStatusCode statusCode,
        string content
    ) =>
        new(statusCode) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class CallbackHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request, cancellationToken);
    }
}
