using System.IO.Abstractions;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.Doctor;

internal sealed class DoctorRemoteSourceProbe(
    IFileSystem fileSystem,
    IGitProcessRunner gitProcessRunner,
    IOciRegistryAvailabilityClient ociRegistryClient,
    ISourceCredentialStore credentialStore,
    bool? canReadStoredCredentials = null
) : IDoctorRemoteSourceProbe
{
    private readonly bool _canReadStoredCredentials =
        canReadStoredCredentials ?? OperatingSystem.IsWindows();

    public async Task<DoctorRemoteProbeResult> ProbeGitAsync(
        ProjectConfiguration.GitSource source,
        CancellationToken cancellationToken
    )
    {
        var transport = ClassifyTransport(source.Url);
        if (transport == GitTransport.Insecure)
        {
            return new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Inconclusive,
                "The configured Git transport is plaintext and was not contacted.",
                "Use an HTTPS or SSH repository URL."
            );
        }

        if (transport == GitTransport.Invalid)
        {
            return new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "The configured Git URL could not be probed safely.",
                "Correct the configured repository URL."
            );
        }

        var command = await gitProcessRunner.RunAsync(
            [
                "-c",
                "credential.helper=",
                "-c",
                "credential.interactive=never",
                "-c",
                "core.askPass=",
                "-c",
                "http.extraHeader=",
                "ls-remote",
                "--symref",
                "--exit-code",
                "--",
                source.Url,
                "HEAD",
            ],
            TimeSpan.FromSeconds(DoctorService.NetworkSourceTimeoutSeconds),
            cancellationToken
        );
        if (command.IsSuccess)
        {
            return new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Success,
                "The configured Git repository is reachable."
            );
        }

        var error = command.Error ?? "Git source probe failed.";
        if (error.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "The Git probe timed out.",
                "Verify network, DNS, and repository host availability."
            );
        }

        if (
            transport == GitTransport.Https
            && error.Contains("authentication", StringComparison.OrdinalIgnoreCase)
        )
        {
            return new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Inconclusive,
                "The Git repository requires authentication; doctor did not invoke credential helpers or prompts.",
                "Authenticate with Git, then rerun the project operation."
            );
        }

        return new DoctorRemoteProbeResult(
            DoctorRemoteProbeOutcome.Failure,
            "The Git repository could not be reached.",
            "Verify the repository URL, DNS/TLS configuration, and access permissions."
        );
    }

    public async Task<DoctorRemoteProbeResult> ProbeOciAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string? packId,
        CancellationToken cancellationToken
    )
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var registry))
        {
            return new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "The OCI registry URL is invalid.",
                "Correct the configured registry URL."
            );
        }

        var identity = OciSourceCredentialIdentity.Create(fileSystem, projectDirectory, source);
        var credentialPartition = identity.Value ?? $"lunapack-doctor-invalid:{source.Name}";
        var credential = GetStoredCredential(credentialPartition);
        string? repository = null;
        if (packId is not null)
        {
            var repositoryResult = OciRepositoryTagMapper.CreateRepository(source, packId);
            if (repositoryResult.Value is not { } mappedRepository)
            {
                return new DoctorRemoteProbeResult(
                    DoctorRemoteProbeOutcome.Failure,
                    "The configured OCI repository path could not be validated.",
                    "Correct the source path or requested pack ID."
                );
            }

            repository = mappedRepository;
        }

        var result = await ociRegistryClient.ProbeAsync(
            registry,
            repository,
            credential,
            credentialPartition,
            cancellationToken
        );
        if (result.IsSuccess)
        {
            return new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Success,
                "The OCI registry endpoint is reachable."
            );
        }

        var failure = result.Failure;
        if (failure is null)
        {
            return new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "The OCI registry probe returned no result.",
                "Verify the configured registry endpoint."
            );
        }

        if (
            failure.Kind
                is OciRegistryFailureKind.AuthenticationRequired
                    or OciRegistryFailureKind.AuthorizationDenied
            && credential is null
        )
        {
            return new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Inconclusive,
                "The OCI registry requires credentials that doctor did not open or prompt for.",
                "Use the normal project operation to validate existing registry access."
            );
        }

        return CreateOciFailure(failure);
    }

    private static DoctorRemoteProbeResult CreateOciFailure(OciRegistryFailure failure) =>
        failure.Kind switch
        {
            OciRegistryFailureKind.AuthenticationRequired => new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "Existing OCI credentials were rejected by the registry.",
                "Refresh the source-bound credential through a normal interactive operation."
            ),
            OciRegistryFailureKind.AuthorizationDenied => new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "The registry denied access to the configured identity.",
                "Grant the identity permission to the configured registry path."
            ),
            OciRegistryFailureKind.Timeout => new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "The OCI registry probe timed out.",
                "Verify network, DNS, and registry host availability."
            ),
            OciRegistryFailureKind.Dns => new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "The OCI registry hostname could not be resolved.",
                "Verify DNS configuration and the registry hostname."
            ),
            OciRegistryFailureKind.Tls => new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "TLS verification failed for the OCI registry.",
                "Correct certificate trust; do not disable TLS verification."
            ),
            OciRegistryFailureKind.InvalidResponse => new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "The OCI registry returned an invalid or unsafe response.",
                "Verify the endpoint and registry authentication configuration."
            ),
            _ => new DoctorRemoteProbeResult(
                DoctorRemoteProbeOutcome.Failure,
                "The OCI registry endpoint was unavailable.",
                "Verify the configured endpoint, network access, and registry availability."
            ),
        };

    private OciRegistryCredential? GetStoredCredential(string credentialPartition)
    {
        if (!_canReadStoredCredentials)
        {
            return null;
        }

        var stored = credentialStore.Get(credentialPartition);
        if (!stored.IsSuccess || stored.Value is not { } credential)
        {
            return null;
        }

        return
            string.IsNullOrWhiteSpace(credential.Username)
            || string.IsNullOrWhiteSpace(credential.Password)
            || credential.Username.Any(char.IsControl)
            || credential.Password.Any(char.IsControl)
            ? null
            : credential;
    }

    private static GitTransport ClassifyTransport(string url)
    {
        if (url.StartsWith('-'))
        {
            return GitTransport.Invalid;
        }

        if (Path.IsPathRooted(url))
        {
            return GitTransport.Local;
        }

        if (
            !url.Contains("://", StringComparison.Ordinal)
            && url.IndexOf(':', StringComparison.Ordinal) > 0
        )
        {
            return GitTransport.Secure;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme.ToLowerInvariant() switch
            {
                "https" => GitTransport.Https,
                "ssh" => GitTransport.Secure,
                "file" => GitTransport.Local,
                "http" or "git" => GitTransport.Insecure,
                _ => GitTransport.Invalid,
            };
        }

        return GitTransport.Invalid;
    }

    private enum GitTransport
    {
        Https,
        Secure,
        Local,
        Insecure,
        Invalid,
    }
}
