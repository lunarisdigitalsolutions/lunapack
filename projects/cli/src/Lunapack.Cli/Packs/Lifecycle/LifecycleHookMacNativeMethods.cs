using System.Runtime.InteropServices;

namespace Lunapack.Cli.Packs.Lifecycle;

internal static class LifecycleHookMacNativeMethods
{
    internal const short StartSuspended = 0x0080;

    [DllImport("libc", EntryPoint = "kqueue", SetLastError = true)]
    internal static extern int CreateQueue();

    [DllImport("libc", EntryPoint = "kevent", SetLastError = true)]
    internal static extern int WaitForEvent(
        int queue,
        [In] LifecycleHookMacEvent[] changes,
        int changeCount,
        [Out] LifecycleHookMacEvent[] events,
        int eventCapacity,
        ref LifecycleHookMacTimespec timeout
    );

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static extern int Close(int fileDescriptor);

    [DllImport("libc", EntryPoint = "posix_spawnattr_init", SetLastError = true)]
    internal static extern int InitializeSpawnAttributes(nint attributes);

    [DllImport("libc", EntryPoint = "posix_spawnattr_destroy", SetLastError = true)]
    internal static extern int DestroySpawnAttributes(nint attributes);

    [DllImport("libc", EntryPoint = "posix_spawnattr_setflags", SetLastError = true)]
    internal static extern int SetSpawnFlags(nint attributes, short flags);

    [DllImport("libc", EntryPoint = "posix_spawnp", SetLastError = true)]
    internal static extern int Spawn(
        out int processId,
        nint executable,
        nint fileActions,
        nint attributes,
        nint arguments,
        nint environment
    );
}
