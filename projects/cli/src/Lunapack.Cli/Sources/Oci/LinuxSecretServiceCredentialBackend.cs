using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lunapack.Cli.Sources.Oci;

[SupportedOSPlatform("linux")]
internal sealed class LinuxSecretServiceCredentialBackend : INativeCredentialBackend
{
    private const string SecretServiceLibrary = "libsecret-1.so.0";
    private const string GlibLibrary = "libglib-2.0.so.0";
    private const string Collection = "default";
    private const string Label = "LunaPack OCI registry credential";
    private const string ContentType = "application/octet-stream";
    private const string ApplicationAttribute = "application";
    private const string AccountAttribute = "account";
    private const string ApplicationValue = "com.lunapack";

    public byte[]? Get(string identity)
    {
        var attributes = CreateAttributes(identity);
        try
        {
            var value = SecretPasswordLookupVBinarySync(
                IntPtr.Zero,
                attributes.Table,
                IntPtr.Zero,
                out var error
            );
            if (error != IntPtr.Zero)
            {
                if (value != IntPtr.Zero)
                {
                    SecretValueUnref(value);
                }

                ThrowIfError(error, "read");
            }

            if (value == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var secret = SecretValueGet(value, out var length);
                if (
                    length > NativeCredentialPayload.MaximumPayloadBytes
                    || (length > 0 && secret == IntPtr.Zero)
                )
                {
                    throw new InvalidOperationException(
                        "Stored registry credential exceeds the Secret Service limit."
                    );
                }

                var payload = new byte[(int)length];
                if (payload.Length > 0)
                {
                    Marshal.Copy(secret, payload, 0, payload.Length);
                }

                return payload;
            }
            finally
            {
                SecretValueUnref(value);
            }
        }
        finally
        {
            DestroyAttributes(attributes);
        }
    }

    public void Save(string identity, byte[] payload)
    {
        var attributes = CreateAttributes(identity);
        var contentType = Marshal.StringToCoTaskMemUTF8(ContentType);
        var collection = Marshal.StringToCoTaskMemUTF8(Collection);
        var label = Marshal.StringToCoTaskMemUTF8(Label);
        var secretData = Marshal.AllocHGlobal(payload.Length);
        try
        {
            Marshal.Copy(payload, 0, secretData, payload.Length);
            var value = SecretValueNew(secretData, payload.Length, contentType);
            if (value == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Linux Secret Service could not prepare the registry credential."
                );
            }

            try
            {
                var stored = SecretPasswordStoreVBinarySync(
                    IntPtr.Zero,
                    attributes.Table,
                    collection,
                    label,
                    value,
                    IntPtr.Zero,
                    out var error
                );
                ThrowIfError(error, "save");
                if (stored == 0)
                {
                    throw new InvalidOperationException(
                        "Linux Secret Service could not save the registry credential."
                    );
                }
            }
            finally
            {
                SecretValueUnref(value);
            }
        }
        finally
        {
            Marshal.Copy(new byte[payload.Length], 0, secretData, payload.Length);
            Marshal.FreeHGlobal(secretData);
            Marshal.FreeCoTaskMem(label);
            Marshal.FreeCoTaskMem(collection);
            Marshal.FreeCoTaskMem(contentType);
            DestroyAttributes(attributes);
        }
    }

    public void Remove(string identity)
    {
        var attributes = CreateAttributes(identity);
        try
        {
            var removed = SecretPasswordClearVSync(
                IntPtr.Zero,
                attributes.Table,
                IntPtr.Zero,
                out var error
            );
            ThrowIfError(error, "remove");
            _ = removed;
        }
        finally
        {
            DestroyAttributes(attributes);
        }
    }

    private static (
        IntPtr Table,
        IntPtr ApplicationKey,
        IntPtr Application,
        IntPtr AccountKey,
        IntPtr Account
    ) CreateAttributes(string identity)
    {
        var table = GHashTableNew(IntPtr.Zero, IntPtr.Zero);
        if (table == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Linux Secret Service could not prepare credential attributes."
            );
        }

        IntPtr applicationKey = IntPtr.Zero;
        IntPtr application = IntPtr.Zero;
        IntPtr accountKey = IntPtr.Zero;
        IntPtr account = IntPtr.Zero;
        try
        {
            applicationKey = Marshal.StringToCoTaskMemUTF8(ApplicationAttribute);
            application = Marshal.StringToCoTaskMemUTF8(ApplicationValue);
            accountKey = Marshal.StringToCoTaskMemUTF8(AccountAttribute);
            account = Marshal.StringToCoTaskMemUTF8(identity);
            if (
                GHashTableInsert(table, applicationKey, application) == 0
                || GHashTableInsert(table, accountKey, account) == 0
            )
            {
                throw new InvalidOperationException(
                    "Linux Secret Service could not prepare credential attributes."
                );
            }

            return (table, applicationKey, application, accountKey, account);
        }
        catch
        {
            DestroyAttributes((table, applicationKey, application, accountKey, account));
            throw;
        }
    }

    private static void DestroyAttributes(
        (
            IntPtr Table,
            IntPtr ApplicationKey,
            IntPtr Application,
            IntPtr AccountKey,
            IntPtr Account
        ) attributes
    )
    {
        if (attributes.Table != IntPtr.Zero)
        {
            GHashTableUnref(attributes.Table);
        }

        Marshal.FreeCoTaskMem(attributes.Account);
        Marshal.FreeCoTaskMem(attributes.AccountKey);
        Marshal.FreeCoTaskMem(attributes.Application);
        Marshal.FreeCoTaskMem(attributes.ApplicationKey);
    }

    private static void ThrowIfError(IntPtr error, string operation)
    {
        if (error == IntPtr.Zero)
        {
            return;
        }

        GErrorFree(error);
        throw new InvalidOperationException(
            $"Unable to {operation} an OCI credential with Linux Secret Service."
        );
    }

    [DllImport(
        SecretServiceLibrary,
        EntryPoint = "secret_password_lookupv_binary_sync",
        ExactSpelling = true
    )]
    private static extern IntPtr SecretPasswordLookupVBinarySync(
        IntPtr schema,
        IntPtr attributes,
        IntPtr cancellable,
        out IntPtr error
    );

    [DllImport(
        SecretServiceLibrary,
        EntryPoint = "secret_password_storev_binary_sync",
        ExactSpelling = true
    )]
    private static extern int SecretPasswordStoreVBinarySync(
        IntPtr schema,
        IntPtr attributes,
        IntPtr collection,
        IntPtr label,
        IntPtr value,
        IntPtr cancellable,
        out IntPtr error
    );

    [DllImport(
        SecretServiceLibrary,
        EntryPoint = "secret_password_clearv_sync",
        ExactSpelling = true
    )]
    private static extern int SecretPasswordClearVSync(
        IntPtr schema,
        IntPtr attributes,
        IntPtr cancellable,
        out IntPtr error
    );

    [DllImport(SecretServiceLibrary, EntryPoint = "secret_value_new", ExactSpelling = true)]
    private static extern IntPtr SecretValueNew(IntPtr secret, nint length, IntPtr contentType);

    [DllImport(SecretServiceLibrary, EntryPoint = "secret_value_get", ExactSpelling = true)]
    private static extern IntPtr SecretValueGet(IntPtr value, out nuint length);

    [DllImport(SecretServiceLibrary, EntryPoint = "secret_value_unref", ExactSpelling = true)]
    private static extern void SecretValueUnref(IntPtr value);

    [DllImport(GlibLibrary, EntryPoint = "g_hash_table_new", ExactSpelling = true)]
    private static extern IntPtr GHashTableNew(IntPtr hashFunction, IntPtr keyEqualFunction);

    [DllImport(GlibLibrary, EntryPoint = "g_hash_table_insert", ExactSpelling = true)]
    private static extern int GHashTableInsert(IntPtr table, IntPtr key, IntPtr value);

    [DllImport(GlibLibrary, EntryPoint = "g_hash_table_unref", ExactSpelling = true)]
    private static extern void GHashTableUnref(IntPtr table);

    [DllImport(GlibLibrary, EntryPoint = "g_error_free", ExactSpelling = true)]
    private static extern void GErrorFree(IntPtr error);
}
