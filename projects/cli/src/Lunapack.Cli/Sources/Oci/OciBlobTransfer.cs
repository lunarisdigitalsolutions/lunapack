namespace Lunapack.Cli.Sources.Oci;

internal sealed record OciBlobTransfer(long BytesWritten, string Sha256Digest);
