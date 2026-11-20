using System.Runtime.InteropServices;

namespace Lunapack.Cli.Packs.Lifecycle;

[StructLayout(LayoutKind.Sequential)]
internal struct LifecycleHookJobAccounting
{
    public long TotalUserTime;
    public long TotalKernelTime;
    public long ThisPeriodTotalUserTime;
    public long ThisPeriodTotalKernelTime;
    public uint TotalPageFaultCount;
    public uint TotalProcesses;
    public uint ActiveProcesses;
    public uint TotalTerminatedProcesses;
}
