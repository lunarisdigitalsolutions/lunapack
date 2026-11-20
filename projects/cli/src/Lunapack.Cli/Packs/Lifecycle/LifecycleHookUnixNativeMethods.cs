using System.Runtime.InteropServices;

namespace Lunapack.Cli.Packs.Lifecycle;

internal static class LifecycleHookUnixNativeMethods
{
    internal const int ChildSubreaperOption = 36;
    internal const int NoSuchProcess = 3;
    internal const int SignalKill = 9;

    [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
    internal static extern int SetSessionId();

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    internal static extern int Kill(int processId, int signal);

    [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    internal static extern int WaitProcess(int processId, out int status, int options);

    [DllImport("libc", EntryPoint = "prctl", SetLastError = true)]
    internal static extern int ProcessControl(
        int option,
        ulong argument,
        ulong unused1,
        ulong unused2,
        ulong unused3
    );
}
