namespace Lunapack.Cli.Sources.Oci;

internal interface INativeCredentialBackend
{
    byte[]? Get(string identity);

    void Remove(string identity);

    void Save(string identity, byte[] payload);
}
