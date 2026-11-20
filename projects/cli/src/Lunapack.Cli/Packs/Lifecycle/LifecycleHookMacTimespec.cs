using System.Runtime.InteropServices;

namespace Lunapack.Cli.Packs.Lifecycle;

[StructLayout(LayoutKind.Sequential)]
internal struct LifecycleHookMacTimespec
{
    public nint Seconds;
    public nint Nanoseconds;
}
