using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Lunapack.Cli.Sources.Oci;

internal static class NativeCredentialPayload
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);
    internal const int MaximumComponentBytes = 64 * 1024;
    internal const int MaximumPayloadBytes = MaximumComponentBytes * 2 + sizeof(int) * 2;

    public static byte[] Serialize(OciRegistryCredential credential)
    {
        byte[]? username = null;
        byte[]? password = null;
        try
        {
            username = _strictUtf8.GetBytes(credential.Username);
            password = _strictUtf8.GetBytes(credential.Password);
            if (username.Length > MaximumComponentBytes || password.Length > MaximumComponentBytes)
            {
                throw new ArgumentException(
                    "Registry credential exceeds the storage limit.",
                    nameof(credential)
                );
            }

            var payload = new byte[sizeof(int) * 2 + username.Length + password.Length];
            BinaryPrimitives.WriteInt32LittleEndian(payload, username.Length);
            username.CopyTo(payload, sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(sizeof(int) + username.Length),
                password.Length
            );
            password.CopyTo(payload, sizeof(int) * 2 + username.Length);
            return payload;
        }
        finally
        {
            if (username is not null)
            {
                CryptographicOperations.ZeroMemory(username);
            }

            if (password is not null)
            {
                CryptographicOperations.ZeroMemory(password);
            }
        }
    }

    public static OciRegistryCredential Deserialize(byte[] payload)
    {
        if (payload.Length < sizeof(int) * 2 || payload.Length > MaximumPayloadBytes)
        {
            throw new FormatException("Stored registry credential is invalid.");
        }

        var usernameLength = BinaryPrimitives.ReadInt32LittleEndian(payload);
        if (usernameLength is < 0 or > MaximumComponentBytes)
        {
            throw new FormatException("Stored registry credential is invalid.");
        }

        var passwordLengthOffset = sizeof(int) + usernameLength;
        if (passwordLengthOffset > payload.Length - sizeof(int))
        {
            throw new FormatException("Stored registry credential is invalid.");
        }

        var passwordLength = BinaryPrimitives.ReadInt32LittleEndian(
            payload.AsSpan(passwordLengthOffset)
        );
        if (
            passwordLength is < 0 or > MaximumComponentBytes
            || sizeof(int) * 2L + usernameLength + passwordLength != payload.Length
        )
        {
            throw new FormatException("Stored registry credential is invalid.");
        }

        try
        {
            var username = _strictUtf8.GetString(payload, sizeof(int), usernameLength);
            var password = _strictUtf8.GetString(
                payload,
                passwordLengthOffset + sizeof(int),
                passwordLength
            );
            return new OciRegistryCredential(username, password);
        }
        catch (DecoderFallbackException exception)
        {
            throw new FormatException("Stored registry credential is invalid.", exception);
        }
    }
}
