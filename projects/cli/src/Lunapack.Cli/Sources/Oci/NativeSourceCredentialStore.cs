using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class NativeSourceCredentialStore : ISourceCredentialStore
{
    private readonly Lazy<INativeCredentialBackend> _credentialBackend;

    public NativeSourceCredentialStore(INativeCredentialBackend? credentialBackend = null) =>
        _credentialBackend = new Lazy<INativeCredentialBackend>(() =>
            credentialBackend ?? NativeCredentialStoreFactory.Create()
        );

    public ManifestOperationResult<OciRegistryCredential?> Get(string key)
    {
        byte[]? payload = null;
        try
        {
            payload = _credentialBackend.Value.Get(CreateIdentity(key));
            if (payload is null)
            {
                return ManifestOperationResult<OciRegistryCredential?>.Success(null);
            }

            try
            {
                return ManifestOperationResult<OciRegistryCredential?>.Success(
                    NativeCredentialPayload.Deserialize(payload)
                );
            }
            catch (FormatException)
            {
                return ManifestOperationResult<OciRegistryCredential?>.Failure(
                    "Stored registry credential is invalid."
                );
            }
        }
        catch (Exception exception) when (IsCredentialStoreFailure(exception))
        {
            return ManifestOperationResult<OciRegistryCredential?>.Failure(
                "The operating-system credential store is unavailable."
            );
        }
        finally
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    public ManifestOperationResult<bool> Save(string key, OciRegistryCredential credential)
    {
        byte[] payload;
        try
        {
            payload = NativeCredentialPayload.Serialize(credential);
        }
        catch (ArgumentException)
        {
            return ManifestOperationResult<bool>.Failure(
                "Registry credential exceeds the supported storage limit."
            );
        }

        try
        {
            _credentialBackend.Value.Save(CreateIdentity(key), payload);
            return ManifestOperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (IsCredentialStoreFailure(exception))
        {
            return ManifestOperationResult<bool>.Failure(
                "The operating-system credential store could not save the registry credential."
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public ManifestOperationResult<bool> Remove(string key)
    {
        try
        {
            _credentialBackend.Value.Remove(CreateIdentity(key));
            return ManifestOperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (IsCredentialStoreFailure(exception))
        {
            return ManifestOperationResult<bool>.Failure(
                "The operating-system credential store could not remove the registry credential."
            );
        }
    }

    private static string CreateIdentity(string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        byte[]? digest = null;
        try
        {
            digest = SHA256.HashData(keyBytes);
            return new Guid(digest.AsSpan(0, 16)).ToString("D");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            if (digest is not null)
            {
                CryptographicOperations.ZeroMemory(digest);
            }
        }
    }

    private static bool IsCredentialStoreFailure(Exception exception) =>
        exception
            is InvalidOperationException
                or DllNotFoundException
                or EntryPointNotFoundException
                or PlatformNotSupportedException
                or BadImageFormatException
                or Win32Exception
                or ExternalException;
}
