namespace Lunapack.Cli.Sources.Oci;

internal enum OciRegistryFailureKind
{
    AuthenticationRequired,
    AuthorizationDenied,
    Conflict,
    Dns,
    InvalidResponse,
    NotFound,
    Timeout,
    Tls,
    Unavailable,
}
