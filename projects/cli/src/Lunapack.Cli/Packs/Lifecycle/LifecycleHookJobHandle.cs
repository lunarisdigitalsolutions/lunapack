using Microsoft.Win32.SafeHandles;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed class LifecycleHookJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public LifecycleHookJobHandle(nint existingHandle)
        : base(ownsHandle: true)
    {
        SetHandle(existingHandle);
    }

    protected override bool ReleaseHandle() =>
        LifecycleHookWindowsNativeMethods.CloseHandle(handle);
}
