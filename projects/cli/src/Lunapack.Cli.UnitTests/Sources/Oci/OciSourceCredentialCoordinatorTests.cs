using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Text;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Oci;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Sources.Oci;

[NotInParallel]
public sealed class OciSourceCredentialCoordinatorTests
{
    private static readonly string _projectDirectory = OperatingSystem.IsWindows()
        ? @"C:\project"
        : "/project";

    [Test]
    public async Task Scenario_OciRegistryCredential_ToStringRedactsSecrets()
    {
        var credential = new OciRegistryCredential("private-user", "secret-sentinel");

        var display = credential.ToString();

        await Assert.That(display).DoesNotContain("private-user");
        await Assert.That(display).DoesNotContain("secret-sentinel");
        await Assert.That(display).Contains("[redacted]");
    }

    [Test]
    public async Task ExecuteAsync_WhenNoStoredCredentialExists_UsesAnonymousAccess()
    {
        var store = new FakeCredentialStore();
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("user", "secret"));
        using var coordinator = CreateCoordinator(store, prompt);
        OciRegistryCredential? operationCredential = new("unexpected", "unexpected");

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            CreateSource(),
            credential =>
            {
                operationCredential = credential;
                return Task.FromResult(OciRegistryResult<OciRegistryUnit>.Succeeded(new()));
            },
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(operationCredential).IsNull();
        await Assert.That(store.GetCalls).IsEqualTo(0);
        await Assert.That(prompt.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_WhenLegacyEnvironmentVariablesAreSet_IgnoresThem()
    {
        using var environment = new OciCredentialEnvironment(
            "private",
            "legacy-user-sentinel",
            "legacy-password-sentinel"
        );
        var ansiConsole = new SpectreTestConsole();
        var console = new CliConsole(ansiConsole, CliLogLevel.Info);
        var store = new FakeCredentialStore();
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("user", "secret"));
        using var coordinator = CreateCoordinator(store, prompt, console);
        var attemptedCredentials = new List<OciRegistryCredential?>();

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            CreateSource(),
            credential =>
            {
                attemptedCredentials.Add(credential);
                return Task.FromResult(
                    OciRegistryResult<OciRegistryUnit>.Failed(
                        new(
                            OciRegistryFailureKind.AuthenticationRequired,
                            "authentication required"
                        )
                    )
                );
            },
            CancellationToken.None
        );

        await Assert
            .That(result.Failure?.Kind)
            .IsEqualTo(OciRegistryFailureKind.AuthenticationRequired);
        await Assert
            .That(attemptedCredentials)
            .IsEquivalentTo(new OciRegistryCredential?[] { null });
        await Assert.That(store.GetCalls).IsEqualTo(1);
        await Assert.That(prompt.Calls).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).DoesNotContain("legacy-user-sentinel");
        await Assert.That(ansiConsole.Output).DoesNotContain("legacy-password-sentinel");
    }

    [Test]
    public async Task ExecuteAsync_WhenInteractiveAuthenticationSucceeds_PersistsAndReportsIt()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        var console = new CliConsole(ansiConsole, CliLogLevel.Info);
        var store = new FakeCredentialStore();
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("user", "secret"));
        using var coordinator = CreateCoordinator(store, prompt, console);

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            CreateSource(),
            credential =>
                Task.FromResult(
                    credential is null
                        ? OciRegistryResult<OciRegistryUnit>.Failed(
                            new(
                                OciRegistryFailureKind.AuthenticationRequired,
                                "authentication required"
                            )
                        )
                        : OciRegistryResult<OciRegistryUnit>.Succeeded(new())
                ),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(prompt.Calls).IsEqualTo(1);
        await Assert.That(store.SaveCalls).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).Contains("Authenticated to OCI source 'private'.");
    }

    [Test]
    public async Task ExecuteAsync_WhenStoredCredentialIsRejected_PromptsForReplacement()
    {
        var oldCredential = new OciRegistryCredential("old-user", "old-secret");
        var newCredential = new OciRegistryCredential("new-user", "new-secret");
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        var store = new FakeCredentialStore(oldCredential);
        var prompt = new FakeCredentialPrompt(newCredential);
        using var coordinator = CreateCoordinator(
            store,
            prompt,
            new CliConsole(ansiConsole, CliLogLevel.Info)
        );
        var attemptedCredentials = new List<OciRegistryCredential?>();

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            CreateSource(),
            credential =>
            {
                attemptedCredentials.Add(credential);
                return Task.FromResult(
                    credential is null or { Username: "old-user" }
                        ? OciRegistryResult<OciRegistryUnit>.Failed(
                            new(
                                OciRegistryFailureKind.AuthenticationRequired,
                                "authentication required"
                            )
                        )
                        : OciRegistryResult<OciRegistryUnit>.Succeeded(new())
                );
            },
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(attemptedCredentials)
            .IsEquivalentTo([null, oldCredential, newCredential]);
        await Assert.That(prompt.Calls).IsEqualTo(1);
        await Assert.That(store.SaveCalls).IsEqualTo(1);
        await Assert.That(store.SavedCredential).IsEqualTo(newCredential);
    }

    [Test]
    public async Task ExecuteAsync_WhenCredentialStoreCannotSave_ReportsWarningAfterSuccess()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        var credential = new OciRegistryCredential("user", "secret-sentinel");
        var store = new FakeCredentialStore(failSave: true);
        var prompt = new FakeCredentialPrompt(credential);
        using var coordinator = CreateCoordinator(
            store,
            prompt,
            new CliConsole(ansiConsole, CliLogLevel.Info)
        );

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            CreateSource(),
            value =>
                Task.FromResult(
                    value is null
                        ? OciRegistryResult<OciRegistryUnit>.Failed(
                            new(
                                OciRegistryFailureKind.AuthenticationRequired,
                                "authentication required"
                            )
                        )
                        : OciRegistryResult<OciRegistryUnit>.Succeeded(new())
                ),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(ansiConsole.Output).Contains("credential could not be saved");
        await Assert.That(ansiConsole.Output).DoesNotContain("secret-sentinel");
        await Assert.That(store.SaveCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_WhenCredentialStoreCannotLoad_WarnsAndContinuesWithoutPrompt()
    {
        var ansiConsole = new SpectreTestConsole();
        var store = new FakeCredentialStore(failGet: true);
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("user", "secret"));
        using var coordinator = CreateCoordinator(
            store,
            prompt,
            new CliConsole(ansiConsole, CliLogLevel.Info)
        );

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            CreateSource(),
            _ =>
                Task.FromResult(
                    OciRegistryResult<OciRegistryUnit>.Failed(
                        new(
                            OciRegistryFailureKind.AuthenticationRequired,
                            "authentication required"
                        )
                    )
                ),
            CancellationToken.None
        );

        await Assert
            .That(result.Failure?.Kind)
            .IsEqualTo(OciRegistryFailureKind.AuthenticationRequired);
        await Assert.That(ansiConsole.Output).Contains("credential store is unavailable.");
        await Assert.That(store.GetCalls).IsEqualTo(1);
        await Assert.That(prompt.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_WhenRegistryDeniesAuthorization_DoesNotPromptOrReadStore()
    {
        var store = new FakeCredentialStore();
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("user", "secret"));
        using var coordinator = CreateCoordinator(store, prompt);

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            CreateSource(),
            _ =>
                Task.FromResult(
                    OciRegistryResult<OciRegistryUnit>.Failed(
                        new(OciRegistryFailureKind.AuthorizationDenied, "access denied")
                    )
                ),
            CancellationToken.None
        );

        await Assert
            .That(result.Failure?.Kind)
            .IsEqualTo(OciRegistryFailureKind.AuthorizationDenied);
        await Assert.That(store.GetCalls).IsEqualTo(0);
        await Assert.That(prompt.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_WhenCanceledAfterAuthenticationFailure_DoesNotPrompt()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new FakeCredentialStore();
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("user", "secret"));
        using var coordinator = CreateCoordinator(store, prompt);

        var canceled = false;
        try
        {
            await coordinator.ExecuteAsync(
                _projectDirectory,
                CreateSource(),
                _ =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(
                        OciRegistryResult<OciRegistryUnit>.Failed(
                            new(
                                OciRegistryFailureKind.AuthenticationRequired,
                                "authentication required"
                            )
                        )
                    );
                },
                cancellation.Token
            );
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        await Assert.That(canceled).IsTrue();
        await Assert.That(prompt.Calls).IsEqualTo(0);
        await Assert.That(store.SaveCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_WhenAuthenticatedRequestReturnsNotFound_PersistsCredential()
    {
        var store = new FakeCredentialStore();
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("user", "secret"));
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        using var coordinator = CreateCoordinator(
            store,
            prompt,
            new CliConsole(ansiConsole, CliLogLevel.Info)
        );

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            CreateSource(),
            credential =>
                Task.FromResult(
                    credential is null
                        ? OciRegistryResult<OciRegistryUnit>.Failed(
                            new(
                                OciRegistryFailureKind.AuthenticationRequired,
                                "authentication required"
                            )
                        )
                        : OciRegistryResult<OciRegistryUnit>.Failed(
                            new(OciRegistryFailureKind.NotFound, "not found")
                        )
                ),
            CancellationToken.None
        );

        await Assert.That(result.Failure?.Kind).IsEqualTo(OciRegistryFailureKind.NotFound);
        await Assert.That(prompt.Calls).IsEqualTo(1);
        await Assert.That(store.SaveCalls).IsEqualTo(1);
        await Assert.That(ansiConsole.Output).Contains("Authenticated to OCI source 'private'.");
    }

    [Test]
    public async Task ExecuteAsync_WhenStoredCredentialIsInvalid_DoesNotSendIt()
    {
        var store = new FakeCredentialStore(new OciRegistryCredential("user", "bad\nsecret"));
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("user", "secret"));
        using var coordinator = CreateCoordinator(store, prompt);
        var attemptedCredentials = new List<OciRegistryCredential?>();

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            CreateSource(),
            credential =>
            {
                attemptedCredentials.Add(credential);
                return Task.FromResult(
                    OciRegistryResult<OciRegistryUnit>.Failed(
                        new(
                            OciRegistryFailureKind.AuthenticationRequired,
                            "authentication required"
                        )
                    )
                );
            },
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert
            .That(attemptedCredentials)
            .IsEquivalentTo(new OciRegistryCredential?[] { null });
        await Assert.That(prompt.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_WhenParallelRequestsNeedAuthentication_PromptsOnce()
    {
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        var console = new CliConsole(ansiConsole, CliLogLevel.Info);
        var store = new FakeCredentialStore();
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("user", "secret"));
        using var coordinator = CreateCoordinator(store, prompt, console);
        var initialRequests = 0;
        var bothInitialRequestsStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        async Task<OciRegistryResult<OciRegistryUnit>> Operation(OciRegistryCredential? credential)
        {
            if (credential is not null)
            {
                return OciRegistryResult<OciRegistryUnit>.Succeeded(new());
            }

            if (Interlocked.Increment(ref initialRequests) == 2)
            {
                bothInitialRequestsStarted.TrySetResult();
            }

            await bothInitialRequestsStarted.Task;
            return OciRegistryResult<OciRegistryUnit>.Failed(
                new(OciRegistryFailureKind.AuthenticationRequired, "authentication required")
            );
        }

        var results = await Task.WhenAll(
            coordinator.ExecuteAsync(
                _projectDirectory,
                CreateSource(),
                Operation,
                CancellationToken.None
            ),
            coordinator.ExecuteAsync(
                _projectDirectory,
                CreateSource(),
                Operation,
                CancellationToken.None
            )
        );

        await Assert.That(results.All(result => result.IsSuccess)).IsTrue();
        await Assert.That(prompt.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_WhenAnonymousRegistryReturnsForbiddenChallenge_PromptsAndRetries()
    {
        var source = CreateSource();
        var authorizationSchemes = new List<string?>();
        var client = CreateRegistryClient(
            (request, _) =>
            {
                authorizationSchemes.Add(request.Headers.Authorization?.Scheme);
                return Task.FromResult(
                    request.Headers.Authorization is null
                        ? CreateForbiddenChallenge()
                        : CreateCatalogResponse()
                );
            }
        );
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        var console = new CliConsole(ansiConsole, CliLogLevel.Info);
        var store = new FakeCredentialStore();
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("user", "secret"));
        using var coordinator = CreateCoordinator(store, prompt, console);

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            source,
            credential =>
                client.ListRepositoriesAsync(
                    new Uri(source.Url),
                    credential,
                    "partition",
                    CancellationToken.None
                ),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(prompt.Calls).IsEqualTo(1);
        await Assert.That(store.SaveCalls).IsEqualTo(1);
        await Assert.That(authorizationSchemes.Count).IsEqualTo(3);
        await Assert.That(authorizationSchemes[0]).IsNull();
        await Assert.That(authorizationSchemes[1]).IsNull();
        await Assert.That(authorizationSchemes[2]).IsEqualTo("Basic");
    }

    [Test]
    public async Task ExecuteAsync_WhenStoredCredentialGetsForbiddenChallenge_DoesNotPromptAgain()
    {
        var source = CreateSource();
        var client = CreateRegistryClient((_, _) => Task.FromResult(CreateForbiddenChallenge()));
        var ansiConsole = new SpectreTestConsole();
        ansiConsole.Profile.Capabilities.Interactive = true;
        var console = new CliConsole(ansiConsole, CliLogLevel.Info);
        var store = new FakeCredentialStore(new OciRegistryCredential("user", "secret"));
        var prompt = new FakeCredentialPrompt(new OciRegistryCredential("other", "replacement"));
        using var coordinator = CreateCoordinator(store, prompt, console);

        var result = await coordinator.ExecuteAsync(
            _projectDirectory,
            source,
            credential =>
                client.ListRepositoriesAsync(
                    new Uri(source.Url),
                    credential,
                    "partition",
                    CancellationToken.None
                ),
            CancellationToken.None
        );

        await Assert
            .That(result.Failure?.Kind)
            .IsEqualTo(OciRegistryFailureKind.AuthorizationDenied);
        await Assert.That(prompt.Calls).IsEqualTo(0);
    }

    private static OciDistributionRegistryClient CreateRegistryClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
    ) => new(httpMessageHandlerFactory: () => new CallbackHttpMessageHandler(send));

    private static HttpResponseMessage CreateForbiddenChallenge()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                "{\"errors\":[{\"code\":\"DENIED\",\"message\":\"registry request failed\"}]}",
                Encoding.UTF8,
                "application/json"
            ),
        };
        response.Headers.TryAddWithoutValidation(
            "WWW-Authenticate",
            "Basic realm=\"https://registry.example.test/auth\""
        );
        return response;
    }

    private static HttpResponseMessage CreateCatalogResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"repositories\":[]}", Encoding.UTF8, "application/json"),
        };

    private sealed class CallbackHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request, cancellationToken);
    }

    private static OciSourceCredentialCoordinator CreateCoordinator(
        FakeCredentialStore store,
        FakeCredentialPrompt prompt,
        CliConsole? console = null
    )
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(_projectDirectory);
        return new OciSourceCredentialCoordinator(
            fileSystem,
            store,
            prompt,
            console ?? TestConsole.Create()
        );
    }

    private static ProjectConfiguration.OciSource CreateSource() =>
        new() { Name = "private", Url = "https://registry.example.test" };

    private sealed class FakeCredentialStore(
        OciRegistryCredential? credential = null,
        bool failGet = false,
        bool failSave = false
    ) : ISourceCredentialStore
    {
        public int GetCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public OciRegistryCredential? SavedCredential { get; private set; }

        public ManifestOperationResult<OciRegistryCredential?> Get(string key)
        {
            GetCalls++;
            return failGet
                ? ManifestOperationResult<OciRegistryCredential?>.Failure(
                    "The operating-system credential store is unavailable."
                )
                : ManifestOperationResult<OciRegistryCredential?>.Success(credential);
        }

        public ManifestOperationResult<bool> Remove(string key) =>
            ManifestOperationResult<bool>.Success(true);

        public ManifestOperationResult<bool> Save(string key, OciRegistryCredential credential)
        {
            SaveCalls++;
            SavedCredential = credential;
            return failSave
                ? ManifestOperationResult<bool>.Failure(
                    "The operating-system credential store could not save the registry credential."
                )
                : ManifestOperationResult<bool>.Success(true);
        }
    }

    private sealed class FakeCredentialPrompt(OciRegistryCredential? credential)
        : IOciCredentialPrompt
    {
        public int Calls { get; private set; }

        public ManifestOperationResult<OciRegistryCredential?> Prompt(
            ProjectConfiguration.OciSource source
        )
        {
            Calls++;
            return ManifestOperationResult<OciRegistryCredential?>.Success(credential);
        }
    }

    private sealed class OciCredentialEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal)
        {
            ["LUNAPACK_OCI_SOURCE"] = Environment.GetEnvironmentVariable("LUNAPACK_OCI_SOURCE"),
            ["LUNAPACK_OCI_USERNAME"] = Environment.GetEnvironmentVariable("LUNAPACK_OCI_USERNAME"),
            ["LUNAPACK_OCI_PASSWORD"] = Environment.GetEnvironmentVariable("LUNAPACK_OCI_PASSWORD"),
        };

        public OciCredentialEnvironment(
            string? source = null,
            string? username = null,
            string? password = null
        )
        {
            Environment.SetEnvironmentVariable("LUNAPACK_OCI_SOURCE", source);
            Environment.SetEnvironmentVariable("LUNAPACK_OCI_USERNAME", username);
            Environment.SetEnvironmentVariable("LUNAPACK_OCI_PASSWORD", password);
        }

        public void Dispose()
        {
            foreach (var (name, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
