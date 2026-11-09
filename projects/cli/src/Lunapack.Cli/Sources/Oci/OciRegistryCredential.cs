namespace Lunapack.Cli.Sources.Oci;

internal sealed record OciRegistryCredential(string Username, string Password)
{
    public override string ToString() =>
        "OciRegistryCredential { Username = [redacted], Password = [redacted] }";
}
