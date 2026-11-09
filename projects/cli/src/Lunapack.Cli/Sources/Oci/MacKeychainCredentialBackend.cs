using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Lunapack.Cli.Sources.Oci;

[SupportedOSPlatform("macos")]
internal sealed class MacKeychainCredentialBackend : INativeCredentialBackend
{
    private const string SecurityFramework =
        "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationFramework =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ServiceName = "LunaPack OCI Registry Credential";
    private const int ItemNotFoundStatus = -25300;
    private const int DuplicateItemStatus = -25299;

    public byte[]? Get(string identity)
    {
        var item = FindItem(identity);
        if (item.Item == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (
                item.PasswordLength > NativeCredentialPayload.MaximumPayloadBytes
                || (item.PasswordLength > 0 && item.PasswordData == IntPtr.Zero)
            )
            {
                throw new InvalidOperationException(
                    "Stored registry credential exceeds the Keychain storage limit."
                );
            }

            var payload = new byte[checked((int)item.PasswordLength)];
            if (payload.Length > 0)
            {
                Marshal.Copy(item.PasswordData, payload, 0, payload.Length);
            }

            return payload;
        }
        finally
        {
            ReleaseItem(item);
        }
    }

    public void Save(string identity, byte[] payload)
    {
        var existingItem = FindItem(identity);
        IntPtr servicePointer = IntPtr.Zero;
        IntPtr accountPointer = IntPtr.Zero;
        IntPtr payloadPointer = IntPtr.Zero;
        try
        {
            servicePointer = Marshal.StringToCoTaskMemUTF8(ServiceName);
            accountPointer = Marshal.StringToCoTaskMemUTF8(identity);
            payloadPointer = Marshal.AllocHGlobal(payload.Length);
            Marshal.Copy(payload, 0, payloadPointer, payload.Length);
            SaveItem(
                identity,
                existingItem.Item,
                servicePointer,
                accountPointer,
                (uint)Encoding.UTF8.GetByteCount(identity),
                payloadPointer,
                payload.Length
            );
        }
        finally
        {
            try
            {
                if (existingItem.Item != IntPtr.Zero)
                {
                    ReleaseItem(existingItem);
                }
            }
            finally
            {
                if (payloadPointer != IntPtr.Zero)
                {
                    Marshal.Copy(new byte[payload.Length], 0, payloadPointer, payload.Length);
                    Marshal.FreeHGlobal(payloadPointer);
                }

                Marshal.FreeCoTaskMem(accountPointer);
                Marshal.FreeCoTaskMem(servicePointer);
            }
        }
    }

    private static void SaveItem(
        string identity,
        IntPtr existingItem,
        IntPtr service,
        IntPtr account,
        uint accountNameLength,
        IntPtr password,
        int passwordLength
    )
    {
        if (existingItem != IntPtr.Zero)
        {
            ModifyItem(existingItem, password, passwordLength);
            return;
        }

        var status = AddItem(
            service,
            account,
            accountNameLength,
            password,
            passwordLength,
            out var newItem
        );
        if (status == DuplicateItemStatus)
        {
            if (newItem != IntPtr.Zero)
            {
                CFRelease(newItem);
            }

            var racedItem = FindItem(identity);
            if (racedItem.Item == IntPtr.Zero)
            {
                throw CreateFailure("save", status);
            }

            try
            {
                ModifyItem(racedItem.Item, password, passwordLength);
            }
            finally
            {
                ReleaseItem(racedItem);
            }

            return;
        }

        try
        {
            EnsureStatus(status, "save");
        }
        finally
        {
            if (newItem != IntPtr.Zero)
            {
                CFRelease(newItem);
            }
        }
    }

    public void Remove(string identity)
    {
        var item = FindItem(identity);
        if (item.Item == IntPtr.Zero)
        {
            return;
        }

        try
        {
            EnsureStatus(SecKeychainItemDelete(item.Item), "remove");
        }
        finally
        {
            ReleaseItem(item);
        }
    }

    private static (uint PasswordLength, IntPtr PasswordData, IntPtr Item) FindItem(string identity)
    {
        var servicePointer = Marshal.StringToCoTaskMemUTF8(ServiceName);
        var accountPointer = Marshal.StringToCoTaskMemUTF8(identity);
        try
        {
            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)Encoding.UTF8.GetByteCount(ServiceName),
                servicePointer,
                (uint)Encoding.UTF8.GetByteCount(identity),
                accountPointer,
                out var passwordLength,
                out var passwordData,
                out var item
            );
            if (status == ItemNotFoundStatus)
            {
                return (0, IntPtr.Zero, IntPtr.Zero);
            }

            EnsureStatus(status, "find");
            return (passwordLength, passwordData, item);
        }
        finally
        {
            Marshal.FreeCoTaskMem(accountPointer);
            Marshal.FreeCoTaskMem(servicePointer);
        }
    }

    private static int AddItem(
        IntPtr service,
        IntPtr account,
        uint accountNameLength,
        IntPtr password,
        int passwordLength,
        out IntPtr item
    ) =>
        SecKeychainAddGenericPassword(
            IntPtr.Zero,
            (uint)Encoding.UTF8.GetByteCount(ServiceName),
            service,
            accountNameLength,
            account,
            (uint)passwordLength,
            password,
            out item
        );

    private static void ModifyItem(IntPtr item, IntPtr password, int passwordLength) =>
        EnsureStatus(
            SecKeychainItemModifyAttributesAndData(
                item,
                IntPtr.Zero,
                (uint)passwordLength,
                password
            ),
            "update"
        );

    private static void ReleaseItem((uint PasswordLength, IntPtr PasswordData, IntPtr Item) item)
    {
        try
        {
            if (item.PasswordData != IntPtr.Zero)
            {
                EnsureStatus(SecKeychainItemFreeContent(IntPtr.Zero, item.PasswordData), "release");
            }
        }
        finally
        {
            if (item.Item != IntPtr.Zero)
            {
                CFRelease(item.Item);
            }
        }
    }

    private static void EnsureStatus(int status, string operation)
    {
        if (status != 0)
        {
            throw CreateFailure(operation, status);
        }
    }

    private static InvalidOperationException CreateFailure(string operation, int status) =>
        new($"Unable to {operation} an OCI credential with macOS Keychain ({status}).");

    [DllImport(
        SecurityFramework,
        EntryPoint = "SecKeychainFindGenericPassword",
        ExactSpelling = true
    )]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength,
        IntPtr serviceName,
        uint accountNameLength,
        IntPtr accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr item
    );

    [DllImport(
        SecurityFramework,
        EntryPoint = "SecKeychainAddGenericPassword",
        ExactSpelling = true
    )]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        IntPtr serviceName,
        uint accountNameLength,
        IntPtr accountName,
        uint passwordLength,
        IntPtr passwordData,
        out IntPtr item
    );

    [DllImport(
        SecurityFramework,
        EntryPoint = "SecKeychainItemModifyAttributesAndData",
        ExactSpelling = true
    )]
    private static extern int SecKeychainItemModifyAttributesAndData(
        IntPtr item,
        IntPtr attributes,
        uint length,
        IntPtr data
    );

    [DllImport(SecurityFramework, EntryPoint = "SecKeychainItemDelete", ExactSpelling = true)]
    private static extern int SecKeychainItemDelete(IntPtr item);

    [DllImport(SecurityFramework, EntryPoint = "SecKeychainItemFreeContent", ExactSpelling = true)]
    private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

    [DllImport(CoreFoundationFramework, EntryPoint = "CFRelease", ExactSpelling = true)]
    private static extern void CFRelease(IntPtr value);
}
