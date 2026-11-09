namespace Lunapack.Cli.Sources.Oci;

internal sealed record OciRegistryFailure(OciRegistryFailureKind Kind, string Message);
