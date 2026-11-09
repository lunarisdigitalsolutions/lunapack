using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lunapack.Cli.Sources.Oci;

[SupportedOSPlatform("windows")]
internal sealed class WindowsCredentialBackend : INativeCredentialBackend
{
    private const uint GenericCredentialType = 1;
    private const uint LocalMachinePersistence = 2;
    private const int NotFoundError = 1168;
    private const int MaximumCredentialBlobBytes = 5 * 512;

    public byte[]? Get(string identity)
    {
        var targetPointer = Marshal.StringToCoTaskMemUni(CreateTarget(identity));
        try
        {
            if (!CredRead(targetPointer, GenericCredentialType, 0, out var credentialPointer))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == NotFoundError)
                {
                    return null;
                }

                throw CreateFailure("read", error);
            }

            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
                if (
                    credential.CredentialBlobSize > NativeCredentialPayload.MaximumPayloadBytes
                    || credential.CredentialBlobSize > MaximumCredentialBlobBytes
                    || (
                        credential.CredentialBlobSize > 0
                        && credential.CredentialBlob == IntPtr.Zero
                    )
                )
                {
                    throw new InvalidOperationException(
                        "Stored registry credential exceeds the Windows credential limit."
                    );
                }

                var payload = new byte[checked((int)credential.CredentialBlobSize)];
                if (payload.Length > 0)
                {
                    Marshal.Copy(credential.CredentialBlob, payload, 0, payload.Length);
                }

                return payload;
            }
            finally
            {
                CredFree(credentialPointer);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(targetPointer);
        }
    }

    public void Save(string identity, byte[] payload)
    {
        if (payload.Length > MaximumCredentialBlobBytes)
        {
            throw new InvalidOperationException(
                "Registry credential exceeds the Windows credential limit."
            );
        }

        var targetPointer = Marshal.StringToCoTaskMemUni(CreateTarget(identity));
        var payloadPointer = Marshal.AllocHGlobal(payload.Length);
        try
        {
            Marshal.Copy(payload, 0, payloadPointer, payload.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredentialType,
                TargetName = targetPointer,
                CredentialBlobSize = (uint)payload.Length,
                CredentialBlob = payloadPointer,
                Persist = LocalMachinePersistence,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw CreateFailure("save", Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            Marshal.Copy(new byte[payload.Length], 0, payloadPointer, payload.Length);
            Marshal.FreeHGlobal(payloadPointer);
            Marshal.FreeCoTaskMem(targetPointer);
        }
    }

    public void Remove(string identity)
    {
        var targetPointer = Marshal.StringToCoTaskMemUni(CreateTarget(identity));
        try
        {
            if (CredDelete(targetPointer, GenericCredentialType, 0))
            {
                return;
            }

            var error = Marshal.GetLastPInvokeError();
            if (error != NotFoundError)
            {
                throw CreateFailure("remove", error);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(targetPointer);
        }
    }

    private static string CreateTarget(string identity) => $"LunaPack/OCI/{identity}";

    private static InvalidOperationException CreateFailure(string operation, int error) =>
        new($"Unable to {operation} an OCI credential with Windows Credential Manager ({error}).");

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        IntPtr targetName,
        uint type,
        uint flags,
        out IntPtr credential
    );

    [DllImport(
        "Advapi32.dll",
        EntryPoint = "CredWriteW",
        ExactSpelling = true,
        SetLastError = true
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport(
        "Advapi32.dll",
        EntryPoint = "CredDeleteW",
        ExactSpelling = true,
        SetLastError = true
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(IntPtr targetName, uint type, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", ExactSpelling = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public uint LastWrittenLowDateTime;
        public uint LastWrittenHighDateTime;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
