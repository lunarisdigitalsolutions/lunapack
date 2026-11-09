using System.Collections.Concurrent;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class OciSourceCredentialCoordinator(
    IFileSystem fileSystem,
    ISourceCredentialStore credentialStore,
    IOciCredentialPrompt credentialPrompt,
    CliConsole console
) : IDisposable
{
    private readonly ConcurrentDictionary<string, AuthenticationSession> _sessions = new(
        StringComparer.Ordinal
    );

    public async Task<OciRegistryResult<T>> ExecuteAsync<T>(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        Func<OciRegistryCredential?, Task<OciRegistryResult<T>>> operation,
        CancellationToken cancellationToken
    )
        where T : class
    {
        var identity = OciSourceCredentialIdentity.Create(fileSystem, projectDirectory, source);
        if (identity.Value is not { } key)
        {
            return Failure<T>(
                OciRegistryFailureKind.InvalidResponse,
                identity.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        if (source.Username is { } configuredUsername && !IsValidUsername(configuredUsername))
        {
            return Failure<T>(
                OciRegistryFailureKind.InvalidResponse,
                $"OCI source '{source.Name}' has an invalid username."
            );
        }

        var environmentCredential = ResolveEnvironmentCredential(source);
        if (environmentCredential.Failure is { } environmentFailure)
        {
            return OciRegistryResult<T>.Failed(environmentFailure);
        }

        if (environmentCredential.Credential is { } suppliedCredential)
        {
            return await operation(suppliedCredential);
        }

        var session = _sessions.GetOrAdd(key, _ => new AuthenticationSession());
        var credential = await GetInMemoryCredentialAsync(session, cancellationToken);
        var result = await operation(credential);
        if (
            result.IsSuccess
            || result.Failure?.Kind != OciRegistryFailureKind.AuthenticationRequired
        )
        {
            return result;
        }

        await MarkRejectedAsync(session, credential, cancellationToken);

        return await RetryAfterAuthenticationFailureAsync(
            session,
            key,
            source,
            credential,
            result,
            operation,
            cancellationToken
        );
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
    }

    private async Task<OciRegistryResult<T>> RetryAfterAuthenticationFailureAsync<T>(
        AuthenticationSession session,
        string key,
        ProjectConfiguration.OciSource source,
        OciRegistryCredential? failedCredential,
        OciRegistryResult<T> failedResult,
        Func<OciRegistryCredential?, Task<OciRegistryResult<T>>> operation,
        CancellationToken cancellationToken
    )
        where T : class
    {
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            LoadStoredCredential(session, key);
            if (
                session.Credential is { } updatedCredential
                && !session.Rejected
                && !ReferenceEquals(updatedCredential, failedCredential)
            )
            {
                var retried = await operation(updatedCredential);
                if (
                    retried.IsSuccess
                    || retried.Failure?.Kind != OciRegistryFailureKind.AuthenticationRequired
                )
                {
                    return retried;
                }

                session.Rejected = true;
            }

            if (session.PromptAttempted || !console.IsInteractive)
            {
                return failedResult;
            }

            session.PromptAttempted = true;
            var prompted = credentialPrompt.Prompt(source);
            if (prompted.Value is not { } replacement)
            {
                return prompted.Error is null
                    ? failedResult
                    : Failure<T>(OciRegistryFailureKind.AuthenticationRequired, prompted.Error);
            }

            if (!IsValidCredential(replacement))
            {
                return Failure<T>(
                    OciRegistryFailureKind.AuthenticationRequired,
                    "Registry credentials must include a non-empty username and password without control characters."
                );
            }

            var authenticated = await operation(replacement);
            if (
                authenticated.IsSuccess
                || authenticated.Failure?.Kind == OciRegistryFailureKind.NotFound
            )
            {
                session.Credential = replacement;
                session.Initialized = true;
                session.Rejected = false;
                console.Success($"Authenticated to OCI source '{source.Name}'.");
                var saved = credentialStore.Save(key, replacement);
                if (!saved.IsSuccess)
                {
                    console.Warning(
                        "Registry access succeeded, but the credential could not be saved to the operating-system store; it will not be reused after this command."
                    );
                }
            }
            else
            {
                session.Rejected = true;
            }

            return authenticated;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private static async Task<OciRegistryCredential?> GetInMemoryCredentialAsync(
        AuthenticationSession session,
        CancellationToken cancellationToken
    )
    {
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            return session.Rejected ? null : session.Credential;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private void LoadStoredCredential(AuthenticationSession session, string key)
    {
        if (session.Initialized)
        {
            return;
        }

        var stored = credentialStore.Get(key);
        session.Initialized = true;
        if (stored.Value is { } credential && IsValidCredential(credential))
        {
            session.Credential = credential;
        }
        else if (stored.IsSuccess)
        {
            if (stored.Value is not null)
            {
                console.Warning("The saved registry credential is invalid; continuing without it.");
            }
        }
        else
        {
            console.Warning(
                $"{stored.Error ?? "The operating-system credential store is unavailable."} Continuing without saved registry credentials."
            );
        }
    }

    private static async Task MarkRejectedAsync(
        AuthenticationSession session,
        OciRegistryCredential? credential,
        CancellationToken cancellationToken
    )
    {
        if (credential is null)
        {
            return;
        }

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            if (ReferenceEquals(session.Credential, credential))
            {
                session.Rejected = true;
            }
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private static EnvironmentCredential ResolveEnvironmentCredential(
        ProjectConfiguration.OciSource source
    )
    {
        var selectedSource = Environment.GetEnvironmentVariable("LUNAPACK_OCI_SOURCE");
        var username = Environment.GetEnvironmentVariable("LUNAPACK_OCI_USERNAME");
        var password = Environment.GetEnvironmentVariable("LUNAPACK_OCI_PASSWORD");
        var hasUsername = !string.IsNullOrEmpty(username);
        var hasPassword = !string.IsNullOrEmpty(password);
        var hasSelectedSource = !string.IsNullOrWhiteSpace(selectedSource);

        if (
            hasSelectedSource
            && !string.Equals(selectedSource, source.Name, StringComparison.Ordinal)
        )
        {
            return new EnvironmentCredential(null, null);
        }

        if (!hasUsername && !hasPassword && !hasSelectedSource)
        {
            return new EnvironmentCredential(null, null);
        }

        if (!hasUsername || !hasPassword || !hasSelectedSource)
        {
            return new EnvironmentCredential(
                null,
                new OciRegistryFailure(
                    OciRegistryFailureKind.AuthenticationRequired,
                    "LUNAPACK_OCI_SOURCE, LUNAPACK_OCI_USERNAME, and LUNAPACK_OCI_PASSWORD must be supplied together."
                )
            );
        }

        var credential = new OciRegistryCredential(username!, password!);
        return IsValidCredential(credential)
            ? new EnvironmentCredential(credential, null)
            : new EnvironmentCredential(
                null,
                new OciRegistryFailure(
                    OciRegistryFailureKind.AuthenticationRequired,
                    "Environment registry credentials must include a non-empty username and password without control characters."
                )
            );
    }

    private static bool IsValidCredential(OciRegistryCredential credential) =>
        IsValidUsername(credential.Username)
        && !string.IsNullOrEmpty(credential.Password)
        && !credential.Password.Any(char.IsControl);

    private static bool IsValidUsername(string username) =>
        !string.IsNullOrWhiteSpace(username) && !username.Any(char.IsControl);

    private static OciRegistryResult<T> Failure<T>(OciRegistryFailureKind kind, string message)
        where T : class => OciRegistryResult<T>.Failed(new OciRegistryFailure(kind, message));

    private sealed record EnvironmentCredential(
        OciRegistryCredential? Credential,
        OciRegistryFailure? Failure
    );

    private sealed class AuthenticationSession : IDisposable
    {
        public readonly SemaphoreSlim Gate = new(1, 1);

        public OciRegistryCredential? Credential { get; set; }

        public bool Initialized { get; set; }

        public bool PromptAttempted { get; set; }

        public bool Rejected { get; set; }

        public void Dispose() => Gate.Dispose();
    }
}
