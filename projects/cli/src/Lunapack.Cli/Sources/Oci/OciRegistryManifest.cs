namespace Lunapack.Cli.Sources.Oci;

internal sealed record OciRegistryManifest(OciRegistryDescriptor Descriptor, byte[] Content);
