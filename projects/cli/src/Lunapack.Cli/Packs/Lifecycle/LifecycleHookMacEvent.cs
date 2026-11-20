using System.Runtime.InteropServices;

namespace Lunapack.Cli.Packs.Lifecycle;

[StructLayout(LayoutKind.Sequential)]
internal struct LifecycleHookMacEvent
{
    public nuint Identity;
    public short Filter;
    public ushort Flags;
    public uint FilterFlags;
    public nint Data;
    public nint UserData;
}
