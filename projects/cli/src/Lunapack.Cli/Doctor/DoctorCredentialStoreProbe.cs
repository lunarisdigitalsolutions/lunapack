using System.Runtime.InteropServices;

namespace Lunapack.Cli.Doctor;

internal sealed class DoctorCredentialStoreProbe : IDoctorCredentialStoreProbe
{
    public DoctorCredentialStoreProbeResult Probe()
    {
        if (OperatingSystem.IsWindows())
        {
            return TryLoad("advapi32.dll", "Windows Credential Manager");
        }

        if (OperatingSystem.IsMacOS())
        {
            return TryLoad(
                "/System/Library/Frameworks/Security.framework/Security",
                "macOS Keychain"
            );
        }

        if (OperatingSystem.IsLinux())
        {
            var secretService = TryLoadLibrary("libsecret-1.so.0");
            var glib = TryLoadLibrary("libglib-2.0.so.0");
            return secretService && glib
                ? new DoctorCredentialStoreProbeResult(
                    true,
                    "Linux Secret Service libraries are available; credentials were not opened."
                )
                : new DoctorCredentialStoreProbeResult(
                    false,
                    "Linux Secret Service libraries could not be loaded; credentials were not opened."
                );
        }

        return new DoctorCredentialStoreProbeResult(
            false,
            "The operating system has no supported native credential backend."
        );
    }

    private static DoctorCredentialStoreProbeResult TryLoad(
        string libraryName,
        string backendName
    ) =>
        TryLoadLibrary(libraryName)
            ? new DoctorCredentialStoreProbeResult(
                true,
                $"{backendName} libraries are available; credentials were not opened."
            )
            : new DoctorCredentialStoreProbeResult(
                false,
                $"{backendName} libraries could not be loaded; credentials were not opened."
            );

    private static bool TryLoadLibrary(string libraryName)
    {
        if (!NativeLibrary.TryLoad(libraryName, out var handle))
        {
            return false;
        }

        NativeLibrary.Free(handle);
        return true;
    }
}
