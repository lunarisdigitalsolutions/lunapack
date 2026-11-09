using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class OciDistributionRegistryClient(
    TimeSpan? timeout = null,
    IOciAccessTokenProvider? accessTokenProvider = null,
    Func<HttpMessageHandler>? httpMessageHandlerFactory = null
) : IOciRegistryClient
{
    public const long MaximumMetadataBytes = 1024 * 1024;
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(45);
    private static readonly SearchValues<char> _lowercaseHexCharacters = SearchValues.Create(
        "0123456789abcdef"
    );
    private const int RegistryPageSize = 100;
    private readonly TimeSpan _timeout = timeout ?? _defaultTimeout;

    public Task<OciRegistryResult<IReadOnlyList<string>>> ListRepositoriesAsync(
        Uri registry,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    ) =>
        ExecuteAsync(
            async token =>
            {
                var registryOrigin = CreateRegistryOrigin(registry);
                var catalogUri = new Uri(
                    $"{registryOrigin.GetLeftPart(UriPartial.Authority)}/v2/_catalog?n={RegistryPageSize}",
                    UriKind.Absolute
                );
                using var httpClient = CreateClient();
                var requestSession = new RegistryRequestSession(
                    httpClient,
                    registryOrigin,
                    credential,
                    credentialPartition,
                    accessTokenProvider
                );
                return await ReadStringListPagesAsync(
                    requestSession,
                    registryOrigin,
                    catalogUri,
                    "repositories",
                    ["registry:catalog:*"],
                    token
                );
            },
            "Unable to enumerate registry repositories.",
            cancellationToken
        );

    public Task<OciRegistryResult<IReadOnlyList<string>>> ListTagsAsync(
        Uri registry,
        string repository,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    ) =>
        ExecuteAsync(
            async token =>
            {
                var registryOrigin = CreateRegistryOrigin(registry);
                var tagsUri = new UriBuilder(
                    CreateRepositoryRequestUri(registry, repository, "tags/list")
                )
                {
                    Query = $"n={RegistryPageSize}",
                }.Uri;
                using var httpClient = CreateClient();
                var requestSession = new RegistryRequestSession(
                    httpClient,
                    registryOrigin,
                    credential,
                    credentialPartition,
                    accessTokenProvider
                );
                return await ReadStringListPagesAsync(
                    requestSession,
                    registryOrigin,
                    tagsUri,
                    "tags",
                    CreateRepositoryScopes(repository, ScopeAction.Pull),
                    token
                );
            },
            "Unable to enumerate repository tags.",
            cancellationToken
        );

    public Task<OciRegistryResult<OciRegistryManifest>> FetchManifestAsync(
        Uri registry,
        string repository,
        string reference,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    ) =>
        ExecuteAsync(
            async token =>
            {
                var registryOrigin = CreateRegistryOrigin(registry);
                using var httpClient = CreateClient();
                var requestSession = new RegistryRequestSession(
                    httpClient,
                    registryOrigin,
                    credential,
                    credentialPartition,
                    accessTokenProvider
                );
                return await FetchManifestCoreAsync(
                    requestSession,
                    registry,
                    repository,
                    reference,
                    token
                );
            },
            "Unable to fetch registry manifest.",
            cancellationToken
        );

    public Task<OciRegistryResult<OciBlobTransfer>> FetchBlobAsync(
        Uri registry,
        string repository,
        OciRegistryDescriptor descriptor,
        Stream destination,
        long maximumBytes,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    ) =>
        ExecuteAsync(
            async token =>
            {
                if (descriptor.Size < 0 || descriptor.Size > maximumBytes)
                {
                    throw new OciRegistryContractException(
                        "Registry blob exceeds the configured limit."
                    );
                }

                var registryOrigin = CreateRegistryOrigin(registry);
                using var httpClient = CreateClient();
                var requestSession = new RegistryRequestSession(
                    httpClient,
                    registryOrigin,
                    credential,
                    credentialPartition,
                    accessTokenProvider
                );
                using var response = await requestSession.SendAsync(
                    () =>
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            CreateRepositoryRequestUri(
                                registry,
                                repository,
                                "blobs",
                                descriptor.Digest
                            )
                        ),
                    CreateRepositoryScopes(repository, ScopeAction.Pull),
                    token
                );
                EnsureResponseStatus(response, HttpStatusCode.OK);
                if (
                    response.Content.Headers.ContentLength is { } contentLength
                    && contentLength != descriptor.Size
                )
                {
                    throw new OciRegistryContractException(
                        "Registry blob size does not match its descriptor."
                    );
                }

                await using var content = await response.Content.ReadAsStreamAsync(token);
                var transfer = await CopyBoundedAsync(
                    content,
                    destination,
                    descriptor.Size,
                    maximumBytes,
                    token
                );
                if (
                    !string.Equals(
                        transfer.Sha256Digest,
                        descriptor.Digest,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new OciRegistryContractException(
                        "Registry blob does not match its descriptor digest."
                    );
                }

                return transfer;
            },
            "Unable to fetch registry blob.",
            cancellationToken
        );

    public Task<OciRegistryResult<OciRegistryUnit>> PushBlobAsync(
        Uri registry,
        string repository,
        OciRegistryDescriptor descriptor,
        Stream content,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    ) =>
        ExecuteAsync(
            token =>
                PushBlobCoreAsync(
                    registry,
                    repository,
                    descriptor,
                    content,
                    credential,
                    credentialPartition,
                    token
                ),
            "Unable to upload registry blob.",
            cancellationToken
        );

    private async Task<OciRegistryUnit> PushBlobCoreAsync(
        Uri registry,
        string repository,
        OciRegistryDescriptor descriptor,
        Stream content,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    )
    {
        if (descriptor.Size < 0)
        {
            throw new OciRegistryContractException("Registry blob descriptor is invalid.");
        }

        await ValidateUploadContentAsync(content, descriptor, cancellationToken);
        content.Position = 0;
        var registryOrigin = CreateRegistryOrigin(registry);
        using var httpClient = CreateClient();
        var requestSession = new RegistryRequestSession(
            httpClient,
            registryOrigin,
            credential,
            credentialPartition,
            accessTokenProvider
        );
        var scopes = CreateRepositoryScopes(repository, ScopeAction.Pull, ScopeAction.Push);
        var blobUri = CreateRepositoryRequestUri(registry, repository, "blobs", descriptor.Digest);
        if (
            await IsBlobAlreadyPresentAsync(
                requestSession,
                blobUri,
                descriptor,
                scopes,
                cancellationToken
            )
        )
        {
            return new OciRegistryUnit();
        }

        var uploadBaseUri = CreateRepositoryRequestUri(registry, repository, "blobs/uploads/");
        var uploadUri = await StartBlobUploadAsync(
            requestSession,
            registryOrigin,
            uploadBaseUri,
            repository,
            descriptor.Digest,
            scopes,
            cancellationToken
        );
        using var uploadResponse = await requestSession.SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, uploadUri)
                {
                    Content = new NonDisposingStreamContent(content, descriptor.Size),
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(
                    "application/octet-stream"
                );
                return request;
            },
            scopes,
            cancellationToken
        );
        EnsureResponseStatus(uploadResponse, HttpStatusCode.Created);
        ValidateResponseDigest(uploadResponse, descriptor.Digest);
        return new OciRegistryUnit();
    }

    private static async Task<bool> IsBlobAlreadyPresentAsync(
        RegistryRequestSession requestSession,
        Uri blobUri,
        OciRegistryDescriptor descriptor,
        IReadOnlyList<string> requestedScopes,
        CancellationToken cancellationToken
    )
    {
        using var response = await requestSession.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Head, blobUri),
            requestedScopes,
            cancellationToken
        );
        if (response.StatusCode == HttpStatusCode.OK)
        {
            ValidateExistingBlobResponse(response, descriptor);
            return true;
        }

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            EnsureResponseStatus(response, HttpStatusCode.NotFound);
        }

        return false;
    }

    private static async Task<Uri> StartBlobUploadAsync(
        RegistryRequestSession requestSession,
        Uri registryOrigin,
        Uri uploadBaseUri,
        string repository,
        string digest,
        IReadOnlyList<string> requestedScopes,
        CancellationToken cancellationToken
    )
    {
        using var response = await requestSession.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, uploadBaseUri),
            requestedScopes,
            cancellationToken
        );
        EnsureResponseStatus(response, HttpStatusCode.Accepted);
        return CreateUploadUri(
            registryOrigin,
            uploadBaseUri,
            response.Headers.Location,
            digest,
            repository
        );
    }

    public Task<OciRegistryResult<OciRegistryUnit>> PushManifestAsync(
        Uri registry,
        string repository,
        OciRegistryDescriptor descriptor,
        Stream content,
        string tag,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    ) =>
        ExecuteAsync(
            async token =>
            {
                if (descriptor.Size is < 0 or > MaximumMetadataBytes)
                {
                    throw new OciRegistryContractException(
                        "Registry manifest metadata exceeds the configured limit."
                    );
                }

                var manifestContent = await ReadBoundedAsync(content, MaximumMetadataBytes, token);
                ValidateDescriptor(descriptor, manifestContent);
                var registryOrigin = CreateRegistryOrigin(registry);
                var requestUri = CreateRepositoryRequestUri(registry, repository, "manifests", tag);
                using var httpClient = CreateClient();
                var requestSession = new RegistryRequestSession(
                    httpClient,
                    registryOrigin,
                    credential,
                    credentialPartition,
                    accessTokenProvider
                );
                using var response = await requestSession.SendAsync(
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
                        {
                            Content = new ByteArrayContent(manifestContent),
                        };
                        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
                            descriptor.MediaType
                        );
                        return request;
                    },
                    CreateRepositoryScopes(repository, ScopeAction.Pull, ScopeAction.Push),
                    token
                );
                EnsureResponseStatus(response, HttpStatusCode.Created);
                ValidateResponseDigest(response, descriptor.Digest);
                return new OciRegistryUnit();
            },
            "Unable to publish registry manifest.",
            cancellationToken
        );

    public Task<OciRegistryResult<OciRegistryDescriptor>> ResolveManifestAsync(
        Uri registry,
        string repository,
        string reference,
        OciRegistryCredential? credential,
        string credentialPartition,
        CancellationToken cancellationToken
    ) =>
        ExecuteAsync(
            async token =>
            {
                var registryOrigin = CreateRegistryOrigin(registry);
                using var httpClient = CreateClient();
                var requestSession = new RegistryRequestSession(
                    httpClient,
                    registryOrigin,
                    credential,
                    credentialPartition,
                    accessTokenProvider
                );
                var manifest = await FetchManifestCoreAsync(
                    requestSession,
                    registry,
                    repository,
                    reference,
                    token
                );
                return manifest.Descriptor;
            },
            "Unable to resolve registry manifest.",
            cancellationToken
        );

    private static async Task<IReadOnlyList<string>> ReadStringListPagesAsync(
        RegistryRequestSession requestSession,
        Uri registryOrigin,
        Uri firstPageUri,
        string propertyName,
        IReadOnlyList<string> requestedScopes,
        CancellationToken cancellationToken
    )
    {
        var values = new List<string>();
        var visitedPages = new HashSet<string>(StringComparer.Ordinal) { firstPageUri.AbsoluteUri };
        var pageUri = firstPageUri;
        while (true)
        {
            using var response = await requestSession.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, pageUri),
                requestedScopes,
                cancellationToken
            );
            EnsureResponseStatus(response, HttpStatusCode.OK);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var content = await ReadBoundedAsync(stream, MaximumMetadataBytes, cancellationToken);
            values.AddRange(ReadJsonStringList(content, propertyName));

            var nextPageUri = ParseNextPageUri(response, registryOrigin, pageUri);
            if (nextPageUri is null)
            {
                return values;
            }

            if (!visitedPages.Add(nextPageUri.AbsoluteUri))
            {
                throw new OciRegistryContractException(
                    "Registry pagination returned a repeated page."
                );
            }

            pageUri = nextPageUri;
        }
    }

    private static async Task<OciRegistryManifest> FetchManifestCoreAsync(
        RegistryRequestSession requestSession,
        Uri registry,
        string repository,
        string reference,
        CancellationToken cancellationToken
    )
    {
        using var response = await requestSession.SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    CreateRepositoryRequestUri(registry, repository, "manifests", reference)
                );
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(OciPackMediaTypes.Manifest)
                );
                return request;
            },
            CreateRepositoryScopes(repository, ScopeAction.Pull),
            cancellationToken
        );
        EnsureResponseStatus(response, HttpStatusCode.OK);

        if (response.Content.Headers.ContentLength is { } contentLength)
        {
            if (contentLength is < 0 or > MaximumMetadataBytes)
            {
                throw new OciRegistryContractException(
                    "Registry metadata exceeds the configured limit."
                );
            }
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            throw new OciRegistryContractException("Registry manifest media type is missing.");
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var bytes = await ReadBoundedAsync(content, MaximumMetadataBytes, cancellationToken);
        if (
            response.Content.Headers.ContentLength is { } declaredLength
            && declaredLength != bytes.LongLength
        )
        {
            throw new OciRegistryContractException(
                "Registry manifest size does not match its content length."
            );
        }

        var descriptor = OciPackArtifact.CreateDescriptor(mediaType, bytes);
        ValidateResponseDigest(response, descriptor.Digest);
        ValidateDescriptor(descriptor, bytes);
        return new OciRegistryManifest(descriptor, bytes);
    }

    private static Uri CreateRepositoryRequestUri(
        Uri registry,
        string repository,
        string endpoint,
        string? reference = null
    )
    {
        ValidateRepository(repository);
        var path = $"/v2/{repository}/{endpoint}";
        if (reference is not null)
        {
            path = $"{path}/{ValidateReference(reference)}";
        }

        return new UriBuilder(CreateRegistryOrigin(registry)) { Path = path }.Uri;
    }

    private static string[] CreateRepositoryScopes(string repository, params ScopeAction[] actions)
    {
        ValidateRepository(repository);
        return
        [
            .. actions.Select(action =>
                $"repository:{repository}:{action.ToString().ToLowerInvariant()}"
            ),
        ];
    }

    private static void ValidateRepository(string repository)
    {
        if (
            string.IsNullOrEmpty(repository)
            || repository.Length > 255
            || repository.StartsWith('/')
            || repository.EndsWith('/')
        )
        {
            throw new OciRegistryContractException("Registry repository name is invalid.");
        }

        foreach (var component in repository.Split('/'))
        {
            if (
                component.Length == 0
                || component is "." or ".."
                || !IsRepositoryCharacter(component[0], allowSeparators: false)
                || !IsRepositoryCharacter(component[^1], allowSeparators: false)
                || component.Any(character =>
                    !IsRepositoryCharacter(character, allowSeparators: true)
                )
            )
            {
                throw new OciRegistryContractException("Registry repository name is invalid.");
            }
        }
    }

    private static bool IsRepositoryCharacter(char character, bool allowSeparators) =>
        character is >= 'a' and <= 'z'
        || character is >= '0' and <= '9'
        || allowSeparators && character is '.' or '_' or '-';

    private static string ValidateReference(string reference)
    {
        if (reference.StartsWith("sha256:", StringComparison.Ordinal))
        {
            if (
                reference.Length != 71
                || reference.AsSpan("sha256:".Length).IndexOfAnyExcept(_lowercaseHexCharacters) >= 0
            )
            {
                throw new OciRegistryContractException("Registry reference is invalid.");
            }

            return reference;
        }

        if (
            reference.Length is < 1 or > 128
            || !IsTagCharacter(reference[0], allowPunctuation: false)
            || reference.Any(character => !IsTagCharacter(character, allowPunctuation: true))
        )
        {
            throw new OciRegistryContractException("Registry reference is invalid.");
        }

        return reference;
    }

    private static bool IsTagCharacter(char character, bool allowPunctuation) =>
        char.IsAsciiLetterOrDigit(character)
        || character == '_'
        || allowPunctuation && character is '.' or '-';

    private static Uri CreateUploadUri(
        Uri registryOrigin,
        Uri requestUri,
        Uri? location,
        string digest,
        string repository
    )
    {
        if (location is null)
        {
            throw new OciRegistryContractException("Registry upload location is invalid.");
        }

        Uri uploadUri;
        try
        {
            uploadUri = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        }
        catch (UriFormatException)
        {
            throw new OciRegistryContractException("Registry upload location is invalid.");
        }

        if (!HasSameOrigin(registryOrigin, uploadUri) || !string.IsNullOrEmpty(uploadUri.UserInfo))
        {
            throw new OciRegistryContractException("Registry upload location is invalid.");
        }

        var uploadPathPrefix = $"/v2/{repository}/blobs/uploads/";
        if (!uploadUri.AbsolutePath.StartsWith(uploadPathPrefix, StringComparison.Ordinal))
        {
            throw new OciRegistryContractException("Registry upload location is invalid.");
        }

        var builder = new UriBuilder(uploadUri);
        var query = builder.Query.TrimStart('?');
        var encodedDigest = Uri.EscapeDataString(digest);
        if (
            query
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Any(parameter =>
                    string.Equals(
                        parameter.Split('=', 2)[0],
                        "digest",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
        )
        {
            throw new OciRegistryContractException("Registry upload location is invalid.");
        }

        builder.Query = string.IsNullOrEmpty(query)
            ? $"digest={encodedDigest}"
            : $"{query}&digest={encodedDigest}";
        return builder.Uri;
    }

    private static async Task ValidateUploadContentAsync(
        Stream content,
        OciRegistryDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        if (!content.CanSeek)
        {
            throw new OciRegistryContractException(
                "Registry blob content must be seekable for integrity validation."
            );
        }

        var originalPosition = content.Position;
        try
        {
            content.Position = 0;
            var transfer = await CopyBoundedAsync(
                content,
                Stream.Null,
                descriptor.Size,
                descriptor.Size,
                cancellationToken
            );
            if (!string.Equals(transfer.Sha256Digest, descriptor.Digest, StringComparison.Ordinal))
            {
                throw new OciRegistryContractException(
                    "Registry blob content does not match its descriptor digest."
                );
            }
        }
        finally
        {
            content.Position = originalPosition;
        }
    }

    private static void ValidateExistingBlobResponse(
        HttpResponseMessage response,
        OciRegistryDescriptor descriptor
    )
    {
        if (
            response.Content.Headers.ContentLength is { } contentLength
            && contentLength != descriptor.Size
        )
        {
            throw new OciRegistryContractException(
                "Existing registry blob size does not match its descriptor."
            );
        }

        ValidateResponseDigest(response, descriptor.Digest);
    }

    private static void ValidateResponseDigest(HttpResponseMessage response, string digest)
    {
        if (
            response.Headers.TryGetValues("Docker-Content-Digest", out var digests)
            && digests.FirstOrDefault() is { Length: > 0 } responseDigest
            && !string.Equals(responseDigest, digest, StringComparison.Ordinal)
        )
        {
            throw new OciRegistryContractException("Registry returned a different content digest.");
        }
    }

    private static List<string> ReadJsonStringList(byte[] content, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OciRegistryContractException("Registry list metadata is invalid.");
            }

            if (!root.TryGetProperty(propertyName, out var values))
            {
                throw new OciRegistryContractException("Registry list metadata is invalid.");
            }

            if (values.ValueKind == JsonValueKind.Null)
            {
                return [];
            }

            if (values.ValueKind != JsonValueKind.Array)
            {
                throw new OciRegistryContractException("Registry list metadata is invalid.");
            }

            var result = new List<string>();
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text)
                {
                    throw new OciRegistryContractException("Registry list metadata is invalid.");
                }

                result.Add(text);
            }

            return result;
        }
        catch (JsonException)
        {
            throw new OciRegistryContractException("Registry list metadata is invalid.");
        }
    }

    private static Uri? ParseNextPageUri(
        HttpResponseMessage response,
        Uri registryOrigin,
        Uri requestUri
    )
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        var link = values.FirstOrDefault();
        if (string.IsNullOrEmpty(link) || !link.StartsWith('<'))
        {
            throw new OciRegistryContractException("Registry pagination link is invalid.");
        }

        var closingBracket = link.IndexOf('>');
        var responseUri = response.RequestMessage?.RequestUri ?? requestUri;
        if (
            closingBracket <= 1
            || responseUri is null
            || !Uri.TryCreate(responseUri, link[1..closingBracket], out var nextPageUri)
            || nextPageUri is null
            || !HasSameOrigin(registryOrigin, nextPageUri)
            || !string.IsNullOrEmpty(nextPageUri.UserInfo)
        )
        {
            throw new OciRegistryContractException("Registry pagination link is invalid.");
        }

        return nextPageUri;
    }

    private static bool HasSameOrigin(Uri expected, Uri actual) =>
        string.Equals(expected.Scheme, actual.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(expected.IdnHost, actual.IdnHost, StringComparison.OrdinalIgnoreCase)
        && expected.Port == actual.Port;

    private static void EnsureResponseStatus(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus
    )
    {
        if (response.StatusCode != expectedStatus)
        {
            throw new OciRegistryResponseException(response.StatusCode);
        }
    }

    private static Uri CreateRegistryOrigin(Uri registry)
    {
        if (
            !registry.IsAbsoluteUri
            || !string.Equals(
                registry.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase
            )
            || !string.IsNullOrEmpty(registry.UserInfo)
        )
        {
            throw new OciRegistryContractException("OCI registry URL is invalid.");
        }

        return new UriBuilder(registry)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty,
        }.Uri;
    }

    private HttpClient CreateClient() =>
        new(
            httpMessageHandlerFactory?.Invoke()
                ?? new SocketsHttpHandler { AllowAutoRedirect = true }
        )
        {
            Timeout = _timeout,
        };

    private async Task<OciRegistryResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationMessage,
        CancellationToken cancellationToken
    )
        where T : class
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutSource.CancelAfter(_timeout);
        try
        {
            return OciRegistryResult<T>.Succeeded(await operation(timeoutSource.Token));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failure<T>(OciRegistryFailureKind.Timeout, $"{operationMessage} Timed out.");
        }
        catch (OciRegistryResponseException exception)
        {
            var failureKind = ClassifyResponse(exception.StatusCode);
            return Failure<T>(failureKind, $"{operationMessage} {DescribeFailure(failureKind)}.");
        }
        catch (HttpRequestException exception)
        {
            var kind = ClassifyRequest(exception);
            return Failure<T>(kind, $"{operationMessage} {DescribeFailure(kind)}.");
        }
        catch (AuthenticationException)
        {
            return Failure<T>(
                OciRegistryFailureKind.InvalidResponse,
                $"{operationMessage} The registry returned an invalid or unsafe authentication challenge."
            );
        }
        catch (SocketException exception)
            when (exception.SocketErrorCode
                    is SocketError.HostNotFound
                        or SocketError.TryAgain
                        or SocketError.NoData
            )
        {
            return Failure<T>(
                OciRegistryFailureKind.Dns,
                $"{operationMessage} Registry hostname could not be resolved."
            );
        }
        catch (OciRegistryContractException exception)
        {
            return Failure<T>(OciRegistryFailureKind.InvalidResponse, exception.Message);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        using var output = new MemoryStream();
        await CopyBoundedAsync(
            source,
            output,
            expectedBytes: null,
            maximumBytes,
            cancellationToken
        );
        return output.ToArray();
    }

    private static async Task<OciBlobTransfer> CopyBoundedAsync(
        Stream source,
        Stream destination,
        long? expectedBytes,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long bytesWritten = 0;
        try
        {
            while (true)
            {
                var count = await source.ReadAsync(buffer, cancellationToken);
                if (count == 0)
                {
                    break;
                }

                bytesWritten += count;
                if (bytesWritten > maximumBytes)
                {
                    throw new OciRegistryContractException(
                        "Registry blob exceeds the configured limit."
                    );
                }

                hash.AppendData(buffer, 0, count);
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        if (expectedBytes is { } size && bytesWritten != size)
        {
            throw new OciRegistryContractException(
                "Registry blob size does not match its descriptor."
            );
        }

        return new OciBlobTransfer(
            bytesWritten,
            $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}"
        );
    }

    private static void ValidateDescriptor(OciRegistryDescriptor descriptor, byte[] content)
    {
        var actualDigest =
            $"sha256:{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}";
        if (
            descriptor.Size != content.LongLength
            || !string.Equals(descriptor.Digest, actualDigest, StringComparison.Ordinal)
        )
        {
            throw new OciRegistryContractException(
                "Registry manifest does not match its descriptor."
            );
        }
    }

    private static OciRegistryResult<T> Failure<T>(OciRegistryFailureKind kind, string message)
        where T : class => OciRegistryResult<T>.Failed(new OciRegistryFailure(kind, message));

    private static OciRegistryFailureKind ClassifyResponse(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => OciRegistryFailureKind.AuthenticationRequired,
            HttpStatusCode.Forbidden => OciRegistryFailureKind.AuthorizationDenied,
            HttpStatusCode.NotFound => OciRegistryFailureKind.NotFound,
            HttpStatusCode.Conflict => OciRegistryFailureKind.Conflict,
            _ => OciRegistryFailureKind.Unavailable,
        };

    private static OciRegistryFailureKind ClassifyRequest(HttpRequestException exception)
    {
        if (exception.HttpRequestError == HttpRequestError.NameResolutionError)
        {
            return OciRegistryFailureKind.Dns;
        }

        if (exception.HttpRequestError == HttpRequestError.SecureConnectionError)
        {
            return OciRegistryFailureKind.Tls;
        }

        return OciRegistryFailureKind.Unavailable;
    }

    private static string DescribeFailure(OciRegistryFailureKind kind) =>
        kind switch
        {
            OciRegistryFailureKind.AuthenticationRequired => "Authentication is required.",
            OciRegistryFailureKind.AuthorizationDenied => "The registry denied permission.",
            OciRegistryFailureKind.Conflict => "The registry rejected a conflicting update.",
            OciRegistryFailureKind.Dns => "Registry hostname could not be resolved.",
            OciRegistryFailureKind.InvalidResponse => "The registry response was invalid.",
            OciRegistryFailureKind.NotFound =>
                "The requested repository or reference was not found.",
            OciRegistryFailureKind.Timeout => "The registry request timed out.",
            OciRegistryFailureKind.Tls => "TLS verification failed.",
            OciRegistryFailureKind.Unavailable => "The registry is unavailable.",
            _ => "The registry request failed.",
        };

    private sealed class RegistryRequestSession(
        HttpClient httpClient,
        Uri registryOrigin,
        OciRegistryCredential? credential,
        string credentialPartition,
        IOciAccessTokenProvider? accessTokenProvider
    )
    {
        private readonly Dictionary<string, AuthenticationHeaderValue> _authorizationCache = new(
            StringComparer.Ordinal
        );

        public async Task<HttpResponseMessage> SendAsync(
            Func<HttpRequestMessage> requestFactory,
            IReadOnlyList<string> requestedScopes,
            CancellationToken cancellationToken
        )
        {
            using var request = requestFactory();
            var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return response;
            }

            return await RetryUnauthorizedRequestAsync(
                requestFactory,
                response,
                requestedScopes,
                cancellationToken
            );
        }

        private async Task<HttpResponseMessage> RetryUnauthorizedRequestAsync(
            Func<HttpRequestMessage> requestFactory,
            HttpResponseMessage initialResponse,
            IReadOnlyList<string> requestedScopes,
            CancellationToken cancellationToken
        )
        {
            var authorization = await ResolveAuthorizationFromResponseAsync(
                initialResponse,
                requestedScopes,
                forceRefresh: false,
                cancellationToken
            );
            if (authorization is null)
            {
                return initialResponse;
            }

            initialResponse.Dispose();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var authenticatedResponse = await SendWithAuthorizationAsync(
                    requestFactory,
                    authorization,
                    cancellationToken
                );
                if (authenticatedResponse.StatusCode != HttpStatusCode.Unauthorized || attempt == 1)
                {
                    return authenticatedResponse;
                }

                var refreshedAuthorization = await ResolveAuthorizationFromResponseAsync(
                    authenticatedResponse,
                    requestedScopes,
                    forceRefresh: true,
                    cancellationToken
                );

                if (refreshedAuthorization is null || refreshedAuthorization.Equals(authorization))
                {
                    return authenticatedResponse;
                }

                authenticatedResponse.Dispose();
                authorization = refreshedAuthorization;
            }

            throw new InvalidOperationException(
                "Registry authentication retry limit was exceeded."
            );
        }

        private async Task<HttpResponseMessage> SendWithAuthorizationAsync(
            Func<HttpRequestMessage> requestFactory,
            AuthenticationHeaderValue authorization,
            CancellationToken cancellationToken
        )
        {
            using var request = requestFactory();
            request.Headers.Authorization = authorization;
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
        }

        private async Task<AuthenticationHeaderValue?> ResolveAuthorizationFromResponseAsync(
            HttpResponseMessage response,
            IReadOnlyList<string> requestedScopes,
            bool forceRefresh,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var challenge = ParseAuthenticationChallenge(response);
                return challenge is null
                    ? null
                    : await ResolveAuthorizationAsync(
                        challenge,
                        requestedScopes,
                        forceRefresh,
                        cancellationToken
                    );
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        private async Task<AuthenticationHeaderValue?> ResolveAuthorizationAsync(
            RegistryAuthChallenge challenge,
            IReadOnlyList<string> requestedScopes,
            bool forceRefresh,
            CancellationToken cancellationToken
        )
        {
            if (string.Equals(challenge.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
            {
                return credential is null ? null : CreateBasicAuthorization(credential);
            }

            if (!string.Equals(challenge.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var bearerChallenge = ValidateBearerChallenge(challenge, requestedScopes);
            var cacheKey = string.Join(
                '\0',
                credentialPartition,
                bearerChallenge.Realm.AbsoluteUri,
                bearerChallenge.Service,
                string.Join(' ', bearerChallenge.Scopes)
            );
            if (!forceRefresh && _authorizationCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            string? token = null;
            if (credential is null && accessTokenProvider is not null)
            {
                token = await accessTokenProvider.ResolveAccessTokenAsync(
                    registryOrigin.IdnHost,
                    bearerChallenge.Realm.AbsoluteUri,
                    bearerChallenge.Service,
                    bearerChallenge.Scopes,
                    forceRefresh,
                    cancellationToken
                );
            }

            token ??= await RequestBearerTokenAsync(
                bearerChallenge.Realm,
                bearerChallenge.Service,
                bearerChallenge.Scopes,
                cancellationToken
            );
            if (!IsValidBearerToken(token))
            {
                throw new OciRegistryContractException(
                    "Registry returned an invalid bearer token."
                );
            }

            var authorization = new AuthenticationHeaderValue("Bearer", token);
            _authorizationCache[cacheKey] = authorization;
            return authorization;
        }

        private BearerChallengeDetails ValidateBearerChallenge(
            RegistryAuthChallenge challenge,
            IReadOnlyList<string> requestedScopes
        )
        {
            if (
                !challenge.Parameters.TryGetValue("realm", out var realmText)
                || !Uri.TryCreate(realmText, UriKind.Absolute, out var realm)
                || realm is null
                || !string.Equals(
                    realm.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase
                )
                || !HasSameOrigin(registryOrigin, realm)
                || !string.IsNullOrEmpty(realm.UserInfo)
                || !string.IsNullOrEmpty(realm.Fragment)
                || !string.IsNullOrEmpty(realm.Query)
            )
            {
                throw new OciRegistryContractException(
                    "Registry authentication realm is invalid or untrusted."
                );
            }

            var service = challenge.Parameters.TryGetValue("service", out var serviceValue)
                ? serviceValue
                : registryOrigin.IdnHost;
            if (
                string.IsNullOrWhiteSpace(service)
                || service.Length > 512
                || service.Any(char.IsControl)
            )
            {
                throw new OciRegistryContractException(
                    "Registry authentication service is invalid."
                );
            }

            var scopes = ParseRequestedScopes(challenge, requestedScopes);
            return new BearerChallengeDetails(realm, service, scopes);
        }

        private async Task<string> RequestBearerTokenAsync(
            Uri realm,
            string service,
            IReadOnlyList<string> scopes,
            CancellationToken cancellationToken
        )
        {
            var tokenUri = new UriBuilder(realm)
            {
                Query =
                    $"service={Uri.EscapeDataString(service)}&scope={Uri.EscapeDataString(string.Join(' ', scopes))}",
            }.Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, tokenUri);
            if (credential is not null)
            {
                request.Headers.Authorization = CreateBasicAuthorization(credential);
            }

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            EnsureResponseStatus(response, HttpStatusCode.OK);
            if (
                response.Content.Headers.ContentLength is { } contentLength
                && contentLength > MaximumMetadataBytes
            )
            {
                throw new OciRegistryContractException(
                    "Registry authentication response exceeds the configured limit."
                );
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            var bytes = await ReadBoundedAsync(content, MaximumMetadataBytes, cancellationToken);
            if (
                response.Content.Headers.ContentLength is { } declaredLength
                && declaredLength != bytes.LongLength
            )
            {
                throw new OciRegistryContractException(
                    "Registry authentication response size is invalid."
                );
            }

            try
            {
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                if (
                    root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("access_token", out var tokenElement)
                        && !root.TryGetProperty("token", out tokenElement)
                    || tokenElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrEmpty(tokenElement.GetString())
                )
                {
                    throw new OciRegistryContractException(
                        "Registry authentication response is invalid."
                    );
                }

                return tokenElement.GetString()!;
            }
            catch (JsonException)
            {
                throw new OciRegistryContractException(
                    "Registry authentication response is invalid."
                );
            }
        }
    }

    private static RegistryAuthChallenge? ParseAuthenticationChallenge(HttpResponseMessage response)
    {
        ICollection<AuthenticationHeaderValue> challenges;
        try
        {
            challenges = response.Headers.WwwAuthenticate;
        }
        catch (FormatException)
        {
            throw new OciRegistryContractException("Registry authentication challenge is invalid.");
        }

        var challenge =
            challenges.FirstOrDefault(value =>
                string.Equals(value.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            )
            ?? challenges.FirstOrDefault(value =>
                string.Equals(value.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            );
        if (challenge is null)
        {
            return null;
        }

        var parameters = string.Equals(
            challenge.Scheme,
            "Bearer",
            StringComparison.OrdinalIgnoreCase
        )
            ? ParseAuthenticationParameters(challenge.Parameter ?? string.Empty)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new RegistryAuthChallenge(challenge.Scheme, parameters);
    }

    private static Dictionary<string, string> ParseAuthenticationParameters(string value)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        while (position < value.Length)
        {
            while (
                position < value.Length
                && (value[position] == ',' || char.IsWhiteSpace(value[position]))
            )
            {
                position++;
            }

            if (position == value.Length)
            {
                break;
            }

            var (key, parameterValue) = ParseAuthenticationParameter(value, ref position);
            if (!parameters.TryAdd(key, parameterValue))
            {
                throw new OciRegistryContractException(
                    "Registry authentication challenge is invalid."
                );
            }

            if (position < value.Length)
            {
                position++;
            }
        }

        return parameters;
    }

    private static (string Key, string Value) ParseAuthenticationParameter(
        string value,
        ref int position
    )
    {
        var keyStart = position;
        while (position < value.Length && IsAuthenticationTokenCharacter(value[position]))
        {
            position++;
        }

        if (keyStart == position)
        {
            throw new OciRegistryContractException("Registry authentication challenge is invalid.");
        }

        var key = value[keyStart..position];
        SkipAuthenticationWhitespace(value, ref position);
        if (position == value.Length || value[position++] != '=')
        {
            throw new OciRegistryContractException("Registry authentication challenge is invalid.");
        }

        SkipAuthenticationWhitespace(value, ref position);
        var parameterValue =
            position < value.Length && value[position] == '"'
                ? ReadQuotedAuthenticationParameter(value, ref position)
                : ReadUnquotedAuthenticationParameter(value, ref position);
        return (key, parameterValue);
    }

    private static string ReadQuotedAuthenticationParameter(string value, ref int position)
    {
        position++;
        var builder = new StringBuilder();
        while (position < value.Length)
        {
            var character = value[position++];
            if (character == '\\')
            {
                if (position == value.Length)
                {
                    break;
                }

                builder.Append(value[position++]);
            }
            else if (character == '"')
            {
                SkipAuthenticationWhitespace(value, ref position);
                if (position < value.Length && value[position] != ',')
                {
                    throw new OciRegistryContractException(
                        "Registry authentication challenge is invalid."
                    );
                }

                return builder.ToString();
            }
            else if (char.IsControl(character))
            {
                throw new OciRegistryContractException(
                    "Registry authentication challenge is invalid."
                );
            }
            else
            {
                builder.Append(character);
            }
        }

        throw new OciRegistryContractException("Registry authentication challenge is invalid.");
    }

    private static string ReadUnquotedAuthenticationParameter(string value, ref int position)
    {
        var start = position;
        while (position < value.Length && value[position] != ',')
        {
            position++;
        }

        var parameterValue = value[start..position].Trim();
        if (
            parameterValue.Length == 0
            || parameterValue.Any(char.IsControl)
            || parameterValue.Contains('"')
        )
        {
            throw new OciRegistryContractException("Registry authentication challenge is invalid.");
        }

        return parameterValue;
    }

    private static void SkipAuthenticationWhitespace(string value, ref int position)
    {
        while (position < value.Length && char.IsWhiteSpace(value[position]))
        {
            position++;
        }
    }

    private static bool IsAuthenticationTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character)
        || character
            is '!'
                or '#'
                or '$'
                or '%'
                or '&'
                or '\''
                or '*'
                or '+'
                or '-'
                or '.'
                or '^'
                or '_'
                or '`'
                or '|'
                or '~';

    private static string[] ParseRequestedScopes(
        RegistryAuthChallenge challenge,
        IReadOnlyList<string> allowedScopes
    )
    {
        var scopeValue = challenge.Parameters.TryGetValue("scope", out var challengeScope)
            ? challengeScope
            : string.Join(' ', allowedScopes);
        var scopes = scopeValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (
            scopes.Length is 0 or > 16
            || scopes.Any(scope =>
                string.IsNullOrWhiteSpace(scope)
                || scope.Length > 2048
                || scope.Any(char.IsControl)
                || !IsScopeAllowed(scope, allowedScopes)
            )
        )
        {
            throw new OciRegistryContractException("Registry authentication scope is invalid.");
        }

        return scopes;
    }

    private static bool IsScopeAllowed(string scope, IReadOnlyList<string> allowedScopes)
    {
        if (allowedScopes.Contains(scope, StringComparer.Ordinal))
        {
            return true;
        }

        var separator = scope.LastIndexOf(':');
        if (separator <= 0 || separator == scope.Length - 1)
        {
            return false;
        }

        var resource = scope[..separator];
        var requestedActions = scope[(separator + 1)..].Split(',');
        var allowedActions = allowedScopes
            .Where(allowed => allowed.StartsWith($"{resource}:", StringComparison.Ordinal))
            .Select(allowed => allowed[(resource.Length + 1)..])
            .ToHashSet(StringComparer.Ordinal);
        return requestedActions.Length > 0
            && requestedActions.All(action => allowedActions.Contains(action));
    }

    private static AuthenticationHeaderValue CreateBasicAuthorization(
        OciRegistryCredential credential
    )
    {
        var bytes = Encoding.UTF8.GetBytes($"{credential.Username}:{credential.Password}");
        try
        {
            return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool IsValidBearerToken(string? token) =>
        !string.IsNullOrEmpty(token)
        && token.Length <= 16 * 1024
        && token.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '.' or '_' or '~' or '+' or '/' or '='
        );

    private sealed record BearerChallengeDetails(Uri Realm, string Service, string[] Scopes);

    private sealed record RegistryAuthChallenge(
        string Scheme,
        IReadOnlyDictionary<string, string> Parameters
    );

    private enum ScopeAction
    {
        Pull,
        Push,
    }

    private sealed class NonDisposingStreamContent(Stream source, long length) : HttpContent
    {
        protected override bool TryComputeLength(out long contentLength)
        {
            contentLength = length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, CancellationToken.None);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken
        ) => SerializeToStreamAsync(stream, cancellationToken);

        private Task SerializeToStreamAsync(Stream stream, CancellationToken cancellationToken)
        {
            source.Position = 0;
            return source.CopyToAsync(stream, 81920, cancellationToken);
        }
    }

    private sealed class OciRegistryResponseException(HttpStatusCode statusCode) : Exception
    {
        public HttpStatusCode StatusCode { get; } = statusCode;
    }

    private sealed class OciRegistryContractException(string message) : Exception(message);
}
