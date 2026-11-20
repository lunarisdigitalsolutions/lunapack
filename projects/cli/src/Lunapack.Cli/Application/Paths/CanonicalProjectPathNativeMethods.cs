using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lunapack.Cli.Application.Paths;

internal static class CanonicalProjectPathNativeMethods
{
    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile
    );

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    internal static extern uint GetFinalPathNameByHandle(
        SafeFileHandle fileHandle,
        [Out] char[] filePath,
        uint filePathLength,
        uint flags
    );
}
