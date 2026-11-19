using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Doctor;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.UnitTests.Doctor;

public sealed class DoctorRemoteSourceProbeTests
{
    [Test]
    public async Task ProbeGit_WhenTransportIsPlaintext_DoesNotInvokeGit()
    {
        var git = new RecordingGitProcessRunner();
        var probe = CreateProbe(git);

        var result = await probe.ProbeGitAsync(
            new ProjectConfiguration.GitSource
            {
                Name = "insecure",
                Url = "http://example.com/packs.git",
            },
            CancellationToken.None
        );

        await Assert.That(result.Outcome).IsEqualTo(DoctorRemoteProbeOutcome.Inconclusive);
        await Assert.That(git.InvocationCount).IsEqualTo(0);
    }

    [Test]
    public async Task ProbeGit_WhenSourceUsesUserlessScpSyntax_InvokesGit()
    {
        var git = new RecordingGitProcessRunner();
        var probe = CreateProbe(git);

        var result = await probe.ProbeGitAsync(
            new ProjectConfiguration.GitSource
            {
                Name = "scp",
                Url = "git.example.test:owner/packs.git",
            },
            CancellationToken.None
        );

        await Assert.That(result.Outcome).IsEqualTo(DoctorRemoteProbeOutcome.Success);
        await Assert.That(git.InvocationCount).IsEqualTo(1);
        await Assert.That(git.Arguments).Contains("--");
        await Assert.That(git.Arguments).Contains("http.extraHeader=");
    }

    [Test]
    public async Task ProbeGit_WhenHttpsRequiresCredentials_DisablesInteractiveCredentialPaths()
    {
        var git = new RecordingGitProcessRunner(
            ManifestOperationResult<GitProcessOutput>.Failure(
                "Git exited with code 128: Git authentication failed."
            )
        );
        var probe = CreateProbe(git);

        var result = await probe.ProbeGitAsync(
            new ProjectConfiguration.GitSource
            {
                Name = "private",
                Url = "https://example.com/packs.git",
            },
            CancellationToken.None
        );

        await Assert.That(result.Outcome).IsEqualTo(DoctorRemoteProbeOutcome.Inconclusive);
        await Assert.That(git.Arguments).Contains("-c");
        await Assert.That(git.Arguments).Contains("credential.helper=");
        await Assert.That(git.Arguments).Contains("credential.interactive=never");
        await Assert.That(git.Arguments).Contains("core.askPass=");
    }

    [Test]
    public async Task ProbeOci_WhenAuthenticationIsNeeded_ReadsButNeverWritesCredential()
    {
        var credentials = new RecordingCredentialStore(
            new OciRegistryCredential("user", "secret-value")
        );
        var registry = new RecordingOciRegistryProbe(
            DoctorRemoteProbeFailure(Authentication: true)
        );
        var probe = CreateProbe(
            credentialStore: credentials,
            registryClient: registry,
            canReadStoredCredentials: true
        );

        var result = await probe.ProbeOciAsync(
            Path.GetTempPath(),
            new ProjectConfiguration.OciSource
            {
                Name = "company",
                Url = "https://registry.example.com",
            },
            packId: null,
            cancellationToken: CancellationToken.None
        );

        await Assert.That(credentials.GetCount).IsEqualTo(1);
        await Assert.That(credentials.SaveCount).IsEqualTo(0);
        await Assert.That(credentials.RemoveCount).IsEqualTo(0);
        await Assert.That(registry.Credential).IsNotNull();
        await Assert.That(result.Outcome).IsEqualTo(DoctorRemoteProbeOutcome.Failure);
        await Assert.That(result.Message).DoesNotContain("secret-value");
    }

    [Test]
    public async Task ProbeOci_WhenStoreCannotBeOpenedSafely_UsesAnonymousAccessOnly()
    {
        var credentials = new RecordingCredentialStore(
            new OciRegistryCredential("user", "secret-value")
        );
        var registry = new RecordingOciRegistryProbe(
            DoctorRemoteProbeFailure(Authentication: true)
        );
        var probe = CreateProbe(
            credentialStore: credentials,
            registryClient: registry,
            canReadStoredCredentials: false
        );

        var result = await probe.ProbeOciAsync(
            Path.GetTempPath(),
            new ProjectConfiguration.OciSource
            {
                Name = "company",
                Url = "https://registry.example.com",
            },
            packId: null,
            cancellationToken: CancellationToken.None
        );

        await Assert.That(credentials.GetCount).IsEqualTo(0);
        await Assert.That(registry.Credential).IsNull();
        await Assert.That(result.Outcome).IsEqualTo(DoctorRemoteProbeOutcome.Inconclusive);
    }

    [Test]
    public async Task ProbeOci_WhenPackIsKnown_ProbesTheConfiguredRepository()
    {
        var registry = new RecordingOciRegistryProbe();
        var probe = CreateProbe(registryClient: registry);

        var result = await probe.ProbeOciAsync(
            Path.GetTempPath(),
            new ProjectConfiguration.OciSource
            {
                Name = "company",
                Url = "https://registry.example.com",
                Path = "engineering",
            },
            packId: "backend",
            cancellationToken: CancellationToken.None
        );

        await Assert.That(result.Outcome).IsEqualTo(DoctorRemoteProbeOutcome.Success);
        await Assert.That(registry.Repository).IsEqualTo("engineering/backend");
    }

    private static DoctorRemoteSourceProbe CreateProbe(
        IGitProcessRunner? git = null,
        ISourceCredentialStore? credentialStore = null,
        IOciRegistryAvailabilityClient? registryClient = null,
        bool canReadStoredCredentials = false
    ) =>
        new(
            new FileSystem(),
            git ?? new RecordingGitProcessRunner(),
            registryClient ?? new RecordingOciRegistryProbe(),
            credentialStore ?? new RecordingCredentialStore(null),
            canReadStoredCredentials
        );

    private static OciRegistryResult<OciRegistryUnit> DoctorRemoteProbeFailure(
        bool Authentication
    ) =>
        OciRegistryResult<OciRegistryUnit>.Failed(
            new OciRegistryFailure(
                Authentication
                    ? OciRegistryFailureKind.AuthenticationRequired
                    : OciRegistryFailureKind.Unavailable,
                "secret-value"
            )
        );

    private sealed class RecordingGitProcessRunner(
        ManifestOperationResult<GitProcessOutput>? result = null
    ) : IGitProcessRunner
    {
        public int InvocationCount { get; private set; }

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            InvocationCount++;
            Arguments = arguments;
            return Task.FromResult(
                result
                    ?? ManifestOperationResult<GitProcessOutput>.Success(
                        new GitProcessOutput(string.Empty, string.Empty)
                    )
            );
        }
    }

    private sealed class RecordingOciRegistryProbe(
        OciRegistryResult<OciRegistryUnit>? result = null
    ) : IOciRegistryAvailabilityClient
    {
        public OciRegistryCredential? Credential { get; private set; }

        public string? Repository { get; private set; }

        public Task<OciRegistryResult<OciRegistryUnit>> ProbeAsync(
            Uri registry,
            string? repository,
            OciRegistryCredential? credential,
            string credentialPartition,
            CancellationToken cancellationToken
        )
        {
            Credential = credential;
            Repository = repository;
            return Task.FromResult(
                result ?? OciRegistryResult<OciRegistryUnit>.Succeeded(new OciRegistryUnit())
            );
        }
    }

    private sealed class RecordingCredentialStore(OciRegistryCredential? credential)
        : ISourceCredentialStore
    {
        public int GetCount { get; private set; }

        public int SaveCount { get; private set; }

        public int RemoveCount { get; private set; }

        public ManifestOperationResult<OciRegistryCredential?> Get(string key)
        {
            GetCount++;
            return ManifestOperationResult<OciRegistryCredential?>.Success(credential);
        }

        public ManifestOperationResult<bool> Remove(string key)
        {
            RemoveCount++;
            return ManifestOperationResult<bool>.Success(true);
        }

        public ManifestOperationResult<bool> Save(string key, OciRegistryCredential value)
        {
            SaveCount++;
            return ManifestOperationResult<bool>.Success(true);
        }
    }
}
